// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Authentication;

/// <summary>A run's view token, attachable to its site's origin and nowhere else.</summary>
/// <remarks>
/// <para>
/// The page's credential is bound to a page and applies to a host over HTTPS, or over plaintext to
/// loopback, on any port. A view's is narrower on purpose: one exact origin, scheme and port included,
/// because a token minted for <c>127.0.0.1:8080</c> has no business reaching <c>127.0.0.1:9090</c>.
/// </para>
/// <para>
/// A class rather than a record, for the reason <see cref="PageCredential"/> is one: the generated
/// <c>ToString</c> would print the secret, and this type finds its way into log scopes.
/// </para>
/// </remarks>
internal sealed class ViewCredential : IOutboundCredential {

    private readonly string _value;

    /// <summary>The header a view's token travels in.</summary>
    public const string HeaderName = "Authorization";

    /// <summary>A credential for one run's view of one origin.</summary>
    /// <param name="origin">The site's origin, from <see cref="SiteOptions"/>.</param>
    /// <param name="token">The run's secret, which never leaves this object.</param>
    public ViewCredential(Uri origin, string token) {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Origin = origin;
        _value = token;
    }

    /// <summary>The one origin this credential may be sent to.</summary>
    public Uri Origin { get; }

    /// <inheritdoc/>
    public string Name => "the run's view token";

    /// <inheritdoc/>
    public string Header => HeaderName;

    /// <inheritdoc/>
    public string HeaderValue => $"{ViewToken.HeaderScheme} {_value}";

    /// <inheritdoc/>
    /// <remarks>Scheme, host and port must all match; a path or query on the request does not matter.</remarks>
    public bool AppliesTo(Uri uri) {
        ArgumentNullException.ThrowIfNull(uri);

        return string.Equals(uri.Scheme, Origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.IdnHost, Origin.IdnHost, StringComparison.OrdinalIgnoreCase)
            && uri.Port == Origin.Port;
    }

    /// <summary>Names the credential and the origin it is for, never its value.</summary>
    /// <returns>A line safe to log.</returns>
    public override string ToString() => $"credential {Name} for {Origin.GetLeftPart(UriPartial.Authority)}";
}
