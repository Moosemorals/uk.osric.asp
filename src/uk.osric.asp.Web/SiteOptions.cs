// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Web.Configuration;

/// <summary>Where the host reaches the site it is serving.</summary>
/// <remarks>
/// <para>
/// One value, because a process that does not know its own origin cannot tell "this is us" from "this
/// is somebody else" — and the assistant's view is the caller that needs to know. It is a policy value
/// like every other (`code-policy-values-are-configuration`): the shipped value is the container's own
/// loopback, where the quadlet publishes the port and its health check asks, and <c>make run</c> binds
/// the same port so the shipped value is true in development too.
/// </para>
/// <para>
/// Plaintext is allowed only to loopback. The token a run presents over it never leaves the machine's
/// own network namespace, which is the same trade <c>PageCredential.AppliesTo</c> already makes for an
/// operator-configured credential.
/// </para>
/// </remarks>
internal sealed record SiteOptions {

    /// <summary>The value this repository ships, for the message an operator reads.</summary>
    public const string Recommended = "http://127.0.0.1:8080";

    /// <summary>The origin: a scheme, a host and a port, and nothing after them.</summary>
    public required Uri Origin { get; init; }

    /// <summary>Joins a site-relative path onto the origin.</summary>
    /// <param name="path">A path beginning with one <c>/</c>, with an optional query.</param>
    /// <param name="url">The absolute URL to request, when the path was usable.</param>
    /// <param name="problem">Why the path is not a site-relative path, when it is not.</param>
    /// <returns><see langword="true"/> when <paramref name="url"/> names this site.</returns>
    /// <remarks>
    /// A leading <c>//</c> is refused rather than joined: <see cref="Uri"/> reads it as a
    /// protocol-relative URL and would leave the site, which is the one thing this method exists to
    /// stop.
    /// </remarks>
    public bool TryResolve(string path, out string url, out string? problem) {
        url = string.Empty;
        problem = null;

        if (string.IsNullOrEmpty(path) || path[0] != '/') {
            problem = "The path must begin with /, as in /hello.";

            return false;
        }

        if (path.StartsWith("//", StringComparison.Ordinal)) {
            problem = "The path must not begin with //, which names another host.";

            return false;
        }

        if (path.Contains("://", StringComparison.Ordinal)) {
            problem = "The path must not name a scheme.";

            return false;
        }

        // The origin has no path of its own, so joining is a concatenation that keeps a query intact.
        string candidate = Origin.GetLeftPart(UriPartial.Authority) + path;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Origin.Scheme
            || parsed.Port != Origin.Port
            || !string.IsNullOrEmpty(parsed.Fragment)
            || !string.Equals(parsed.IdnHost, Origin.IdnHost, StringComparison.OrdinalIgnoreCase)) {
            problem = "The path does not name a document on this site.";

            return false;
        }

        url = parsed.AbsoluteUri;

        return true;
    }

    /// <summary>Reads <c>Site</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static SiteOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Site");
        SiteOptions options = FromConfiguration(reader);
        reader.Complete();

        return options;
    }

    /// <summary>Reads <c>Site</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Site</c>, which the caller completes.</param>
    internal static SiteOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        string raw = reader.Text("Origin", $"an origin such as {Recommended}");
        if (raw.Length == 0) {
            // The reader has already said the key is missing or blank; a second complaint about the
            // same key would be noise in the one list an operator reads.
            return new SiteOptions { Origin = new Uri(Recommended) };
        }

        if (!TryParseOrigin(raw, out Uri? origin)) {
            reader.Fail("Origin", $"is \"{raw}\", which is not an origin this site can use. Set it to a scheme and host with no path, query or fragment, such as {Recommended}; plaintext http is accepted only for a loopback host.");

            return new SiteOptions { Origin = new Uri(Recommended) };
        }

        return new SiteOptions { Origin = origin };
    }

    private static bool TryParseOrigin(string raw, out Uri origin) {
        origin = null!;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? parsed)
            || parsed.UserInfo.Length != 0
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.AbsolutePath.Length > 0 && parsed.AbsolutePath != "/") {
            return false;
        }

        bool isHttps = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        bool isLoopbackHttp = string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && parsed.IsLoopback;

        if (!isHttps && !isLoopbackHttp) {
            return false;
        }

        origin = parsed;

        return true;
    }
}
