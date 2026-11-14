// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page render may cost the process that runs it.</summary>
/// <remarks>
/// Every value is read from <c>Pages:Render</c> and none has a fallback: a missing or unusable value
/// stops the site at startup. The deadline is the whole budget for a request: queueing for a slot
/// counts against it, because a visitor is waiting either way.
/// </remarks>
internal sealed record RenderOptions {

    /// <summary>The deepest chain a configuration may ask for.</summary>
    public const int MaximumLayoutDepth = 16;

    /// <summary>The whole budget for one render, including the wait for a slot.</summary>
    public required double TimeoutSeconds { get; init; }

    /// <summary>Page renders allowed to run at once; further requests wait without a thread.</summary>
    public required int Slots { get; init; }

    /// <summary>
    /// How much body a page may buffer before it is refused. It stops at the first flush: a streaming
    /// page's body is on the wire and is not charged after its response has been committed.
    /// </summary>
    public required int MaxBodyChars { get; init; }

    /// <summary>Abandoned renders alive at once before the health check reports unhealthy.</summary>
    public required int AbandonedLimit { get; init; }

    /// <summary>How many layouts one page may be wrapped in, up to <see cref="MaximumLayoutDepth"/>.</summary>
    public required int MaxLayoutDepth { get; init; }

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Render</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static RenderOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Render");
        RenderOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Render</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Render</c>, which the caller completes.</param>
    internal static RenderOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        RenderOptions options = new() {
            TimeoutSeconds = reader.Decimal("TimeoutSeconds", recommended: 5,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 60),
            Slots = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64),
            MaxBodyChars = reader.Number("MaxBodyChars", recommended: 4 * 1024 * 1024,
                minimum: 1024, maximum: 64 * 1024 * 1024),
            AbandonedLimit = reader.Number("AbandonedLimit", recommended: 2, minimum: 1, maximum: 64),
            MaxLayoutDepth = reader.Number("MaxLayoutDepth", recommended: 4, minimum: 1, maximum: MaximumLayoutDepth),
        };
        return options;
    }
}
