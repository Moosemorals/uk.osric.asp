// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>What a page has asked the response to be, before it is built.</summary>
/// <remarks>
/// Collects the status, content type, headers, redirect and script policy so the response is shaped
/// once, when the handler returns or at the first flush. The commit and the layout rule are not here:
/// they are about who is calling, so they stay on the context, which validates before it delegates.
/// </remarks>
internal sealed class PageResponseState {

    // A page must not be able to clobber the shell's own cookies or framing. Everything that changes
    // the meaning of the body is reachable through a dedicated member instead. Caching policy is not
    // a page's to set: a page with code is per-visitor and the host sends no-store.
    private static readonly HashSet<string> s_allowedHeaders = [with(StringComparer.OrdinalIgnoreCase),
        "Content-Language"];

    private const string CustomHeaderPrefix = "X-";

    private readonly Dictionary<string, string> _headers = [with(StringComparer.OrdinalIgnoreCase)];
    private string _contentType;
    private string? _location;
    private int _status = PageResponse.Default.Status;
    private bool _allowsScripts;

    /// <summary>Creates the response a page will ask for.</summary>
    /// <param name="contentType">The media type the page's key names.</param>
    public PageResponseState(string contentType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _contentType = contentType;
    }

    /// <summary>The content type the response will be sent with.</summary>
    public string ContentType => _contentType;

    /// <summary>The redirect target, or <see langword="null"/> when the page did not redirect.</summary>
    public string? Location => _location;

    /// <summary>Sets the status code the response will be sent with.</summary>
    /// <param name="status">The code to send, between 100 and 599.</param>
    public void SetStatus(int status) {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 100, nameof(status));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599, nameof(status));

        _status = status;
    }

    /// <summary>Sets the content type the response will be sent with.</summary>
    /// <param name="contentType">The media type to send.</param>
    public void SetContentType(string contentType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _contentType = contentType;
    }

    /// <summary>Sets a response header the page is allowed to control.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value; it may not contain a line break.</param>
    /// <exception cref="ArgumentException">
    /// The header is reserved to the shell, or the value cannot be sent safely.
    /// </exception>
    public void SetHeader(string name, string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!IsAllowed(name)) {
            throw new ArgumentException($"{name} is set by the site, not by a page.", nameof(name));
        }

        if (value.Contains('\r') || value.Contains('\n')) {
            throw new ArgumentException("A header value may not contain a line break.", nameof(value));
        }

        _headers[name] = value;
    }

    /// <summary>Allows the response to load same-origin scripts.</summary>
    public void AllowScripts() => _allowsScripts = true;

    /// <summary>Redirects instead of writing a body.</summary>
    /// <param name="location">The target, relative or absolute.</param>
    /// <param name="status">The redirect status; 302 by default.</param>
    public void Redirect(string location, int status = 302) {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        if (location.Contains('\r') || location.Contains('\n')) {
            throw new ArgumentException("A redirect target may not contain a line break.", nameof(location));
        }

        SetStatus(status);
        _location = location;
    }

    /// <summary>What the page asked the response to be.</summary>
    /// <remarks>
    /// Every page that runs code is <c>no-store</c>: it can keep per-visitor state, so its body has
    /// no business in a shared cache and no business inviting a conditional request. The host writes
    /// the same header after the page's own, so this is the default rather than the last word.
    /// </remarks>
    public PageResponse ToResponse() {
        _headers.TryAdd("Cache-Control", "no-store");
        return new(_status, _contentType, _headers, _location) { AllowsScripts = _allowsScripts };
    }

    private static bool IsAllowed(string name) {
        // The X- wildcard would otherwise reach the shell's own X-Content-Type-Options and friends,
        // and a page clearing one silently weakens the response it is served in.
        if (SecurityHeaders.IsOwned(name)) {
            return false;
        }

        return s_allowedHeaders.Contains(name)
            || name.StartsWith(CustomHeaderPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
