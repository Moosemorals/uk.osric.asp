// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page's search of the store may cost, and how wide a match's snippet is.</summary>
/// <remarks>
/// Every value comes from <c>Pages:Search</c>. The duration is the owner's bound measured in time: a
/// count bounds the answer, but the scan's work is done whether anything matches. The two limits and
/// the two contexts have the shape the listing's limits have — a default a request may leave out and a
/// limit it is refused above.
/// </remarks>
internal sealed record PageSearchOptions {

    /// <summary>The longest time allowance the site will run with.</summary>
    /// <remarks>
    /// The longest render the site accepts is a minute, so a search longer than that can never finish
    /// inside the thing it serves.
    /// </remarks>
    public const int MaximumDurationMs = 60_000;

    /// <summary>The largest match or context limit the site will run with.</summary>
    public const int MaximumMatches = 10_000;

    /// <summary>The narrowest snippet the site will run with.</summary>
    public const int MinimumContextBytes = 64;

    /// <summary>The widest context limit the site will run with.</summary>
    public const int MaximumContextBytes = 16_384;

    /// <summary>How long one search may run, across the fingerprint, the reads and the matching.</summary>
    public required int MaxDurationMs { get; init; }

    /// <summary>The matches a search answers with when the caller names no limit.</summary>
    public required int DefaultMatches { get; init; }

    /// <summary>The most matches one search may answer with.</summary>
    public required int MaxMatches { get; init; }

    /// <summary>How wide a snippet is when the caller names no context.</summary>
    public required int DefaultContextBytes { get; init; }

    /// <summary>The widest snippet a caller may ask for.</summary>
    public required int MaxContextBytes { get; init; }

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
        int maxContextBytes = reader.Number("MaxContextBytes", recommended: 1_024,
            minimum: MinimumContextBytes, maximum: MaximumContextBytes);
        int defaultContextBytes = reader.Number("DefaultContextBytes", recommended: 64,
            minimum: MinimumContextBytes, maximum: MaximumContextBytes);

        reader.FailIf(defaultMatches > maxMatches, "DefaultMatches",
            $"is {defaultMatches}, which is larger than MaxMatches ({maxMatches})");
        reader.FailIf(defaultContextBytes > maxContextBytes, "DefaultContextBytes",
            $"is {defaultContextBytes}, which is larger than MaxContextBytes ({maxContextBytes})");

        PageSearchOptions options = new() {
            MaxDurationMs = reader.Number("MaxDurationMs", recommended: 60, minimum: 1, maximum: MaximumDurationMs),
            DefaultMatches = defaultMatches,
            MaxMatches = maxMatches,
            DefaultContextBytes = defaultContextBytes,
            MaxContextBytes = maxContextBytes,
        };

        reader.FailIf(reader.Has("DefaultContextChars"), "DefaultContextChars",
            "is no longer read. DefaultContextBytes is a snippet's width in UTF-8 bytes, so rename the key and restart");
        reader.FailIf(reader.Has("MaxContextChars"), "MaxContextChars",
            "is no longer read. MaxContextBytes is a snippet's widest size in UTF-8 bytes, so rename the key and restart");

        return options;
    }
}
