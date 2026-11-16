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
/// this type is the page's door to them and the rules about who may ask. The commit gate, the layout
/// rule, the stream refusals and the antiforgery token are here because each answers "may this layer
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
/// <param name="isLayout">
/// Whether this layer is a layout, in which case the response belongs to the content: status,
/// content type, headers and a redirect are ignored, because a layout is markup and nothing else.
/// </param>
/// <param name="logger">Where a layout's ignored setting is reported.</param>
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
    bool isLayout,
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
    /// This render cannot stream, the page is a layout, it has redirected, or it is still wrapped in a
    /// layout.
    /// </exception>
    public async Task FlushAsync() {
        ThrowIfCannotStream();

        await body.FlushAsync(ToResponse());
    }

    /// <inheritdoc/>
    public void SetStatus(int status) {
        ThrowIfCommitted(nameof(SetStatus));

        if (IgnoreAsLayout(nameof(SetStatus), status.ToString(CultureInfo.InvariantCulture))) {
            return;
        }

        response.SetStatus(status);
    }

    /// <inheritdoc/>
    public void SetContentType(string contentType) {
        ThrowIfCommitted(nameof(SetContentType));

        if (IgnoreAsLayout(nameof(SetContentType), contentType)) {
            return;
        }

        response.SetContentType(contentType);
    }

    /// <inheritdoc/>
    public void SetHeader(string name, string value) {
        ThrowIfCommitted(nameof(SetHeader));

        if (IgnoreAsLayout(nameof(SetHeader), name)) {
            return;
        }

        response.SetHeader(name, value);
    }

    /// <inheritdoc/>
    public void AllowScripts() {
        ThrowIfCommitted(nameof(AllowScripts));

        // The one response setting a layout shares: its script is part of its chrome, so the union
        // with the content's is what the response is served with.
        response.AllowScripts();
    }

    /// <inheritdoc/>
    public void Redirect(string location, int status = 302) {
        ThrowIfCommitted(nameof(Redirect));

        if (IgnoreAsLayout(nameof(Redirect), location)) {
            return;
        }

        // The commit and layout gates are this type's, so the redirect is applied whole by the state
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

    // Validated before the commit: a redirect has no body, a layout cannot stream, and a page that
    // would be wrapped has no place to put the bytes before the layout has run.
    private void ThrowIfCannotStream() {
        if (isLayout) {
            throw new InvalidOperationException($"{path.Key} is a layout; a layout is markup and cannot stream.");
        }

        if (!body.CanStream) {
            throw new InvalidOperationException($"{path.Key} cannot stream its response in this render.");
        }

        if (response.Location is not null) {
            throw new InvalidOperationException($"{path.Key} has redirected; a redirect has no body to stream.");
        }

        if (wouldWrap?.Invoke(response.ContentType) == true) {
            throw new InvalidOperationException(
                $"{path.Key} is wrapped in a layout, which needs its whole body before it can run. Set IPageLayout.Layout to null and write the page's own document before the first flush.");
        }
    }

    private void ThrowIfCommitted(string member) {
        if (body.IsCommitted) {
            throw new InvalidOperationException(
                $"{path.Key} has already flushed its response; {member} must be called before the first flush.");
        }
    }

    // A layout is markup and nothing else. The content ran first and owns the response, so an
    // attempt from a layout is ignored rather than allowed to change what the visitor asked for -
    // and logged, because a silent no-op is a puzzle for whoever wrote the layout.
    private bool IgnoreAsLayout(string member, string detail) {
        if (!isLayout) {
            return false;
        }

        // Checked first so a filtered level costs what it did through the context, rather than a scope
        // the provider will never see.
        if (logger.IsEnabled(LogLevel.Warning)) {
            using IDisposable? scope = logger.BeginScope("Page {Path}", path.Key);
            logger.LogWarning("{Message}", $"{path.Key} called {member}({detail}); a layout cannot set the response.");
        }

        return true;
    }
}
