// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

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

    /// <summary>The longest credential name <c>Pages:Fetch:Credentials</c> accepts.</summary>
    public const int MaximumCredentialNameLength = 64;

    /// <summary>How long one fetch may take.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public required double TimeoutSeconds { get; init; }

    /// <summary>The most decompressed bytes one response may return.</summary>
    public required int MaxResponseBytes { get; init; }

    /// <summary>The most requests one render may make.</summary>
    public required int MaxRequestsPerRender { get; init; }

    /// <summary>The most redirects one fetch may follow; zero disables following.</summary>
    public required int MaxRedirects { get; init; }

    /// <summary>The operator-configured credentials a page may name, by name.</summary>
    public required IReadOnlyDictionary<string, PageCredential> Credentials { get; init; }

    /// <summary>The per-fetch budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Fetch</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageFetchOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Fetch");
        PageFetchOptions options = new() {
            TimeoutSeconds = reader.Decimal("TimeoutSeconds", recommended: 4,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 60),
            MaxResponseBytes = reader.Number("MaxResponseBytes", recommended: 1024 * 1024,
                minimum: 1024, maximum: 64 * 1024 * 1024),
            MaxRequestsPerRender = reader.Number("MaxRequestsPerRender", recommended: 10,
                minimum: 1, maximum: 100),
            MaxRedirects = reader.Number("MaxRedirects", recommended: 3, minimum: 0, maximum: 20),
            Credentials = ReadCredentials(configuration.GetSection("Pages:Fetch:Credentials")),
        };
        reader.Complete();
        return options;
    }

    // The section is optional: no page naming a credential needs one, and the shipped file carries an
    // empty object rather than omitting it only so an operator can see the shape to copy.
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
            throw new ConfigurationException($"{entry.Path}: the name must be 1 to {MaximumCredentialNameLength} characters with no control characters.");
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
            throw new ConfigurationException($"{entry.Path}: Host must be a bare host name with no scheme, path, query or port.");
        }

        return new Uri($"https://{host}/").IdnHost;
    }

    private static string ReadHeader(IConfigurationSection entry) {
        string header = entry["Header"] is { Length: > 0 } configured ? configured : "Authorization";
        if (header.Any(char.IsControl)
            || (!string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)
                && !header.StartsWith("X-", StringComparison.OrdinalIgnoreCase))) {
            throw new ConfigurationException($"{entry.Path}: Header must be Authorization or an X- header.");
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
            throw new ConfigurationException($"{entry.Path}: Scheme must not contain CR, LF or surrounding whitespace.");
        }

        return scheme;
    }

    private static string ReadValue(IConfigurationSection entry) {
        string? value = entry["Value"];
        if (string.IsNullOrEmpty(value) || value.Contains('\r') || value.Contains('\n')) {
            throw new ConfigurationException($"{entry.Path}: Value is required and must not contain CR or LF.");
        }

        return value;
    }

    private static PagePath ReadPage(IConfigurationSection entry) {
        string? page = entry["Page"];
        if (string.IsNullOrWhiteSpace(page)) {
            throw new ConfigurationException($"{entry.Path}: Page is required.");
        }

        PagePath key = PagePath.Normalize(page);
        if (!key.IsAddressable) {
            throw new ConfigurationException($"{entry.Path}: Page must be an addressable page path.");
        }

        return key;
    }
}
