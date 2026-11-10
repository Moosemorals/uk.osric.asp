// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

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

    /// <summary>How long one fetch may take.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public double TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>The most decompressed bytes one response may return.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>The most requests one render may make.</summary>
    public int MaxRequestsPerRender { get; init; } = DefaultMaxRequestsPerRender;

    /// <summary>The most redirects one fetch may follow; zero disables following.</summary>
    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

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
        };
    }
}
