// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page's search of the store may cost, and how wide a match's snippet is.</summary>
/// <remarks>
/// Every value comes from <c>Pages:Search</c>. The duration is the owner's bound measured in time: a
/// count bounds the answer, but the scan's work is done whether anything matches. The two limits and
/// the two contexts have the shape the listing's limits have — a default a request may leave out and a
/// cap it is refused above.
/// </remarks>
internal sealed record PageSearchOptions {

    /// <summary>The longest time allowance the site will run with.</summary>
    /// <remarks>
    /// The longest render the site accepts is a minute, so a search longer than that can never finish
    /// inside the thing it serves.
    /// </remarks>
    public const int MaximumDurationMs = 60_000;

    /// <summary>The largest match or context cap the site will run with.</summary>
    public const int MaximumMatches = 10_000;

    /// <summary>The narrowest snippet the site will run with.</summary>
    public const int MinimumContextChars = 64;

    /// <summary>The widest context cap the site will run with.</summary>
    public const int MaximumContextChars = 16_384;

    /// <summary>How long one search may run, across the fingerprint, the reads and the matching.</summary>
    public required int MaxDurationMs { get; init; }

    /// <summary>The matches a search answers with when the caller names no limit.</summary>
    public required int DefaultMatches { get; init; }

    /// <summary>The most matches one search may answer with.</summary>
    public required int MaxMatches { get; init; }

    /// <summary>How wide a snippet is when the caller names no context.</summary>
    public required int DefaultContextChars { get; init; }

    /// <summary>The widest snippet a caller may ask for.</summary>
    public required int MaxContextChars { get; init; }

    /// <summary>The allowance as a time span.</summary>
    public TimeSpan Duration => TimeSpan.FromMilliseconds(MaxDurationMs);

    /// <summary>Reads <c>Pages:Search</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageSearchOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Search");
        PageSearchOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Search</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Search</c>, which the caller completes.</param>
    internal static PageSearchOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        int maxMatches = reader.Number("MaxMatches", recommended: 1_000, minimum: 1, maximum: MaximumMatches);
        int defaultMatches = reader.Number("DefaultMatches", recommended: 100, minimum: 1, maximum: MaximumMatches);
        int maxContextChars = reader.Number("MaxContextChars", recommended: 1_024,
            minimum: MinimumContextChars, maximum: MaximumContextChars);
        int defaultContextChars = reader.Number("DefaultContextChars", recommended: 64,
            minimum: MinimumContextChars, maximum: MaximumContextChars);

        reader.FailIf(defaultMatches > maxMatches, "DefaultMatches",
            $"is {defaultMatches}, which is larger than MaxMatches ({maxMatches})");
        reader.FailIf(defaultContextChars > maxContextChars, "DefaultContextChars",
            $"is {defaultContextChars}, which is larger than MaxContextChars ({maxContextChars})");

        PageSearchOptions options = new() {
            MaxDurationMs = reader.Number("MaxDurationMs", recommended: 60, minimum: 1, maximum: MaximumDurationMs),
            DefaultMatches = defaultMatches,
            MaxMatches = maxMatches,
            DefaultContextChars = defaultContextChars,
            MaxContextChars = maxContextChars,
        };
        return options;
    }
}
