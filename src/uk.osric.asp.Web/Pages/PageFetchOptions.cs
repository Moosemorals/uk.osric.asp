// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Bounds the host's outbound HTTP, for pages and for the assistant's fetch tool.</summary>
/// <remarks>
/// <para>
/// There is no host allow-list: any absolute <c>http</c> or <c>https</c> URL is reachable, and the
/// guards here are about shape and size rather than destination (DEC-216). The container's egress
/// is not otherwise restricted, so this is a footgun guard, not a sandbox.
/// </para>
/// <para>
/// The timeout is below the default render budget on purpose, so a slow API becomes a value the page
/// can render instead of a <c>504</c>.
/// </para>
/// </remarks>
internal sealed record PageFetchOptions {

    /// <summary>The per-fetch timeout, in seconds, when none is configured.</summary>
    public const double DefaultTimeoutSeconds = 4;

    /// <summary>The largest response body, in bytes, when none is configured.</summary>
    public const int DefaultMaxResponseBytes = 1024 * 1024;

    /// <summary>Requests one render may make, when none is configured.</summary>
    public const int DefaultMaxRequestsPerRender = 10;

    /// <summary>Redirects one fetch may follow, when none is configured.</summary>
    public const int DefaultMaxRedirects = 3;

    /// <summary>The longest credential name <c>Pages:Fetch:Credentials</c> accepts.</summary>
    public const int MaximumCredentialNameLength = 64;

    /// <summary>How long one fetch may take.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public double TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>The most decompressed bytes one response may return.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>The most requests one render may make.</summary>
    public int MaxRequestsPerRender { get; init; } = DefaultMaxRequestsPerRender;

    /// <summary>The most redirects one fetch may follow; zero disables following.</summary>
    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

    /// <summary>The operator-configured credentials a page may name, by name.</summary>
    public IReadOnlyDictionary<string, PageCredential> Credentials { get; init; } =
        new Dictionary<string, PageCredential>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The per-fetch budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Fetch</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use.</returns>
    public static PageFetchOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Fetch");

        return new PageFetchOptions {
            TimeoutSeconds = Math.Max(PageBudget.MinimumTimeoutSeconds, section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds)),
            MaxResponseBytes = Math.Max(1024, section.GetValue("MaxResponseBytes", DefaultMaxResponseBytes)),
            MaxRequestsPerRender = Math.Max(1, section.GetValue("MaxRequestsPerRender", DefaultMaxRequestsPerRender)),
            MaxRedirects = Math.Max(0, section.GetValue("MaxRedirects", DefaultMaxRedirects)),
            Credentials = ReadCredentials(section.GetSection("Credentials")),
        };
    }

    private static IReadOnlyDictionary<string, PageCredential> ReadCredentials(IConfigurationSection section) {
        return section.GetChildren().ToDictionary(
            entry => entry.Key,
            ReadCredential,
            StringComparer.OrdinalIgnoreCase);
    }

    // Every rule fails here, at startup, rather than at the first request: an operator who mistypes a
    // credential should see it in the journal, not in a page that cannot reach its provider. No message
    // includes the value.
    private static PageCredential ReadCredential(IConfigurationSection entry) {
        string name = entry.Key;
        if (name.Length is 0 or > MaximumCredentialNameLength || name.Any(char.IsControl)) {
            throw new InvalidOperationException($"{entry.Path}: the name must be 1 to {MaximumCredentialNameLength} characters with no control characters.");
        }

        return new PageCredential(
            name,
            ReadHost(entry),
            ReadHeader(entry),
            ReadScheme(entry),
            ReadValue(entry),
            ReadPage(entry));
    }

    private static string ReadHost(IConfigurationSection entry) {
        string? host = entry["Host"];
        if (string.IsNullOrWhiteSpace(host)
            || host.Contains('/')
            || host.Contains(':')
            || host.Contains('?')
            || host.Contains('#')
            || host.Contains('@')
            || host.Any(char.IsWhiteSpace)
            || Uri.CheckHostName(host) is UriHostNameType.Unknown) {
            throw new InvalidOperationException($"{entry.Path}: Host must be a bare host name with no scheme, path, query or port.");
        }

        return new Uri($"https://{host}/").IdnHost;
    }

    private static string ReadHeader(IConfigurationSection entry) {
        string header = entry["Header"] is { Length: > 0 } configured ? configured : "Authorization";
        if (header.Any(char.IsControl)
            || (!string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)
                && !header.StartsWith("X-", StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"{entry.Path}: Header must be Authorization or an X- header.");
        }

        return header;
    }

    private static string? ReadScheme(IConfigurationSection entry) {
        string? scheme = entry["Scheme"];
        if (string.IsNullOrEmpty(scheme)) {
            return null;
        }

        if (!string.Equals(scheme, scheme.Trim(), StringComparison.Ordinal)
            || scheme.Contains('\r')
            || scheme.Contains('\n')) {
            throw new InvalidOperationException($"{entry.Path}: Scheme must not contain CR, LF or surrounding whitespace.");
        }

        return scheme;
    }

    private static string ReadValue(IConfigurationSection entry) {
        string? value = entry["Value"];
        if (string.IsNullOrEmpty(value) || value.Contains('\r') || value.Contains('\n')) {
            throw new InvalidOperationException($"{entry.Path}: Value is required and must not contain CR or LF.");
        }

        return value;
    }

    private static PagePath ReadPage(IConfigurationSection entry) {
        string? page = entry["Page"];
        if (string.IsNullOrWhiteSpace(page)) {
            throw new InvalidOperationException($"{entry.Path}: Page is required.");
        }

        PagePath key = PagePath.Normalize(page);
        if (!key.IsAddressable) {
            throw new InvalidOperationException($"{entry.Path}: Page must be an addressable page path.");
        }

        return key;
    }
}
