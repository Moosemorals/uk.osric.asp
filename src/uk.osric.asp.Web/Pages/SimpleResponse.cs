// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The only <see cref="IPageResponse"/> a page ever writes into.</summary>
/// <remarks>
/// <para>
/// What the page asks for is collected by <see cref="PageResponseState"/> and <see cref="PageBody"/>;
/// this type is the page's door to them and the rules about who may ask. The commit gate, the layer
/// role, the stream refusals and the antiforgery token are here because each answers "may this layer
/// set that?" about the response.
/// </para>
/// <para>
/// The response is applied once the handler has returned, or at the first flush, so a page that
/// throws leaves nothing half-sent.
/// </para>
/// </remarks>
/// <param name="response">What the page asked the response to be.</param>
/// <param name="body">The body the page writes, and its way onto the wire.</param>
/// <param name="path">The layer's own key, which the refusals name.</param>
/// <param name="request">The request, for the method a sign-in redirect answers with.</param>
/// <param name="loginRedirect">Where <see cref="RedirectToLogin"/> sends an anonymous visitor.</param>
/// <param name="role">
/// What this layer is. Only the content owns the response: a layout or an embedded page is markup,
/// so status, content type and headers are ignored, and an embedded page's redirect refuses because
/// it has no response to redirect.
/// </param>
/// <param name="logger">Where a layer that does not own the response reports an ignored setting.</param>
/// <param name="antiforgeryField">
/// Builds the hidden antiforgery input, or <see langword="null"/> when the page must get nothing.
/// </param>
/// <param name="wouldWrap">
/// Whether a response of this content type would be wrapped in a layout, in which case it cannot
/// stream.
/// </param>
internal sealed class SimpleResponse(
    PageResponseState response,
    PageBody body,
    PagePath path,
    PageRequest request,
    string loginRedirect,
    PageLayerRole role,
    ILogger logger,
    Func<string>? antiforgeryField,
    Func<string, bool>? wouldWrap) : IPageResponse {

    // Written once, under the body gate, so two reads in one render hand out one token: the token and
    // its cookie are a pair, and minting a second would leave the first unverifiable.
    private string? _antiforgeryField;

    /// <summary>What the page asked the response to be.</summary>
    public PageResponse ToResponse() => response.ToResponse();

    /// <summary>The body written so far.</summary>
    public string GetBody() => body.GetText();

    /// <summary>Whether the first flush has committed the response.</summary>
    public bool IsCommitted => body.IsCommitted;

    /// <summary>Writes whatever the page left buffered after its last flush.</summary>
    public Task CompleteAsync() => body.CompleteAsync();

    /// <summary>Gives the body the render's lifetime, so its I/O and its stream share one clock.</summary>
    /// <param name="lifetime">The lifetime the renderer opened for this request.</param>
    public void AttachLifetime(RenderLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        body.AttachLifetime(lifetime);
    }

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    public void Abandon() => body.Abandon();

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The page passed null.</exception>
    public void Write(string message) {
        ArgumentNullException.ThrowIfNull(message);

        body.Write(WebUtility.HtmlEncode(message));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The page passed null.</exception>
    public void WriteRaw(string message) {
        ArgumentNullException.ThrowIfNull(message);

        body.Write(message);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// This render cannot stream, the page is a layout or an embedded page, it has redirected, or it
    /// has declared a wrapper.
    /// </exception>
    public async Task FlushAsync() {
        ThrowIfCannotStream();

        await body.FlushAsync(ToResponse());
    }

    /// <inheritdoc/>
    public void SetStatus(int status) {
        ThrowIfCommitted(nameof(SetStatus));

        if (IgnoreWhenNotOwning(nameof(SetStatus), status.ToString(CultureInfo.InvariantCulture))) {
            return;
        }

        response.SetStatus(status);
    }

    /// <inheritdoc/>
    public void SetContentType(string contentType) {
        ThrowIfCommitted(nameof(SetContentType));

        if (IgnoreWhenNotOwning(nameof(SetContentType), contentType)) {
            return;
        }

        response.SetContentType(contentType);
    }

    /// <inheritdoc/>
    public void SetHeader(string name, string value) {
        ThrowIfCommitted(nameof(SetHeader));

        if (IgnoreWhenNotOwning(nameof(SetHeader), name)) {
            return;
        }

        response.SetHeader(name, value);
    }

    /// <inheritdoc/>
    public void AllowScripts() {
        ThrowIfCommitted(nameof(AllowScripts));

        // The one response setting a layout shares: its script is part of its chrome, so the chain
        // unions it with the content's to build the response.
        response.AllowScripts();
    }

    /// <inheritdoc/>
    /// <exception cref="UserFacingException">An embedded page cannot redirect.</exception>
    public void Redirect(string location, int status = 302) {
        ThrowIfCommitted(nameof(Redirect));

        // Ignoring it would make the ordinary "if no user, redirect to login" guard vanish into an
        // empty box, so an embedded page that redirects is refused rather than silently dropped.
        if (role == PageLayerRole.Embedded) {
            throw new UserFacingException(
                $"{path.Key} is rendered inside {request.Path} and tried to redirect to {location}; an embedded page has no response of its own.");
        }

        if (IgnoreWhenNotOwning(nameof(Redirect), location)) {
            return;
        }

        // The commit and role gates are this type's, so the redirect is applied whole by the state
        // rather than through SetStatus, which would run those gates a second time.
        response.Redirect(location, status);
    }

    /// <inheritdoc/>
    public void RedirectToLogin() {
        // A form post that turns into a sign-in must not be replayed as a post when the visitor comes
        // back. Pages only answer GET, POST and HEAD, so anything else takes the GET shape.
        int status = string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status303SeeOther
            : StatusCodes.Status302Found;

        Redirect(loginRedirect, status);
    }

    /// <inheritdoc/>
    public string AntiforgeryField {
        get {
            ThrowIfCommitted(nameof(AntiforgeryField));

            if (antiforgeryField is null) {
                return string.Empty;
            }

            return body.MintOnce(ref _antiforgeryField, antiforgeryField);
        }
    }

    // Validated before the commit: a redirect has no body, a layer that does not own the response
    // cannot stream, and a page that declared a wrapper has no place to put the bytes before the
    // wrapper has run.
    private void ThrowIfCannotStream() {
        if (role != PageLayerRole.Content) {
            throw new InvalidOperationException($"{path.Key} is a {Describe(role)}; only the page the visitor asked for can stream.");
        }

        if (!body.CanStream) {
            throw new InvalidOperationException($"{path.Key} cannot stream its response in this render.");
        }

        if (response.Location is not null) {
            throw new InvalidOperationException($"{path.Key} has redirected; a redirect has no body to stream.");
        }

        if (wouldWrap?.Invoke(response.ContentType) == true) {
            throw new InvalidOperationException(
                $"{path.Key} declares a wrapper, which needs its whole body before it can run. A streaming page writes its own document, so it cannot call IPageComposition.Wrap.");
        }
    }

    private void ThrowIfCommitted(string member) {
        if (body.IsCommitted) {
            throw new InvalidOperationException(
                $"{path.Key} has already flushed its response; {member} must be called before the first flush.");
        }
    }

    // Only the content owns the response. A layout or an embedded page is markup and nothing else, so
    // an attempt from one is ignored rather than allowed to change what the visitor asked for - and
    // logged, because a silent no-op is a puzzle for whoever wrote the page. A redirect is the one
    // setting an embedded page is refused for rather than ignored, above.
    private bool IgnoreWhenNotOwning(string member, string detail) {
        if (role == PageLayerRole.Content) {
            return false;
        }

        // Checked first so a filtered level costs what it did through the context, rather than a scope
        // the provider will never see.
        if (logger.IsEnabled(LogLevel.Warning)) {
            using IDisposable? scope = logger.BeginScope("Page {Path}", path.Key);
            logger.LogWarning("{Message}",
                $"{path.Key} called {member}({detail}); a {Describe(role)} cannot set the response.");
        }

        return true;
    }

    private static string Describe(PageLayerRole role) => role switch {
        PageLayerRole.Layout => "layout",
        PageLayerRole.Embedded => "embedded page",
        _ => "page",
    };
}
