// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Configuration;

/// <summary>Bounds and enables a page's outbound HTTP.</summary>
/// <remarks>
/// <para>
/// Fetch is off until an operator lists a host. Entries are bare hosts, matched exactly and
/// case-insensitively: no wildcards, no prefixes, no ports. An entry that names anything else is a
/// startup error rather than a line that silently does nothing.
/// </para>
/// <para>
/// The timeout is below the default render budget on purpose, so a slow API becomes a value the page
/// can render instead of a <c>504</c>.
/// </para>
/// </remarks>
internal sealed record PageFetchOptions {

    /// <summary>The per-fetch timeout, in seconds, when none is configured.</summary>
    public const int DefaultTimeoutSeconds = 4;

    /// <summary>The largest response body, in bytes, when none is configured.</summary>
    public const int DefaultMaxResponseBytes = 1024 * 1024;

    /// <summary>Requests one render may make, when none is configured.</summary>
    public const int DefaultMaxRequestsPerRender = 10;

    /// <summary>Redirects one fetch may follow, when none is configured.</summary>
    public const int DefaultMaxRedirects = 3;

    /// <summary>The hosts a page may fetch from; empty means every fetch is refused.</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    /// <summary>How long one fetch may take.</summary>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>The most decompressed bytes one response may return.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>The most requests one render may make.</summary>
    public int MaxRequestsPerRender { get; init; } = DefaultMaxRequestsPerRender;

    /// <summary>The most redirects one fetch may follow; zero disables following.</summary>
    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

    /// <summary>The per-fetch budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Fetch</c>, refusing the whole configuration when a host is malformed.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use.</returns>
    /// <exception cref="InvalidOperationException">An allowed host is not a bare host name.</exception>
    public static PageFetchOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Fetch");
        List<string> hosts = [];
        foreach (string? host in section.GetSection("AllowedHosts").Get<string[]>() ?? []) {
            hosts.Add(ValidateHost(host));
        }

        return new PageFetchOptions {
            AllowedHosts = hosts,
            TimeoutSeconds = Math.Max(1, section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds)),
            MaxResponseBytes = Math.Max(1024, section.GetValue("MaxResponseBytes", DefaultMaxResponseBytes)),
            MaxRequestsPerRender = Math.Max(1, section.GetValue("MaxRequestsPerRender", DefaultMaxRequestsPerRender)),
            MaxRedirects = Math.Max(0, section.GetValue("MaxRedirects", DefaultMaxRedirects)),
        };
    }

    /// <summary>Whether a host is on the list, matched exactly and case-insensitively.</summary>
    /// <param name="host">A bare host, as <see cref="Uri.IdnHost"/> reports it.</param>
    /// <returns><see langword="true"/> when the host may be fetched from.</returns>
    public bool Allows(string host) {
        ArgumentNullException.ThrowIfNull(host);

        foreach (string allowed in AllowedHosts) {
            if (string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    // A bracketed IPv6 literal is allowed, because the brackets make it unambiguous; anywhere else a
    // colon means a port, and a port is not something a host entry can carry.
    private static string ValidateHost(string? host) {
        string value = host?.Trim() ?? string.Empty;
        if (value.Length == 0) {
            throw new InvalidOperationException("Pages:Fetch:AllowedHosts may not contain an empty entry.");
        }

        if (value.Contains('/', StringComparison.Ordinal)
            || value.Contains('?', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal)
            || value.Contains("://", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Pages:Fetch:AllowedHosts entry '{value}' must be a bare host, with no scheme, path or query.");
        }

        bool bracketed = value.StartsWith('[') && value.EndsWith(']');
        if (!bracketed && value.Contains(':', StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Pages:Fetch:AllowedHosts entry '{value}' must not carry a port; the host is the trust boundary.");
        }

        return value;
    }
}
