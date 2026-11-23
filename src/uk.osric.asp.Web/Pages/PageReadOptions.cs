// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page's read of the store may cost.</summary>
/// <remarks>
/// Every value comes from <c>Pages:Read</c>. The row limit is the store's limit; the source rate is
/// shared by the whole layout chain, on the same terms as the body and fetch allowances
/// (`pages-one-request-one-chain`), and a job gets a rate of its own from <c>Pages:Jobs</c>. A source
/// read is a rate with a burst rather than a count (`pages-allowances-are-a-rate`).
/// </remarks>
internal sealed record PageReadOptions {

    /// <summary>The largest row limit the site will run with.</summary>
    public const int MaximumRowsPerList = 10_000;

    /// <summary>The smallest source rate the site will run with.</summary>
    public const int MinimumSourceCharsPerSecond = 1024;

    /// <summary>The key that sets the rate, named in a refusal.</summary>
    public const string RateKey = "Pages:Read:SourceCharsPerSecond";

    /// <summary>The most rows one listing may return.</summary>
    public required int MaxRowsPerList { get; init; }

    /// <summary>The rate one render may pull template text in at, across every layer.</summary>
    /// <remarks>
    /// The burst is what this rate spends in a whole render, derived from
    /// <c>Pages:Render:TimeoutSeconds</c>. A read is whole, so the burst is also the largest single
    /// template that can ever be read.
    /// </remarks>
    public required int SourceCharsPerSecond { get; init; }

    /// <summary>Reads <c>Pages:Read</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageReadOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Read");
        PageReadOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Read</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Read</c>, which the caller completes.</param>
    internal static PageReadOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        PageReadOptions options = new() {
            MaxRowsPerList = reader.Number("MaxRowsPerList", recommended: 500,
                minimum: 1, maximum: MaximumRowsPerList),
            SourceCharsPerSecond = reader.Number("SourceCharsPerSecond", recommended: 104_858,
                minimum: MinimumSourceCharsPerSecond, maximum: 16 * 1024 * 1024),
        };

        // A count from the release before this one is refused rather than ignored.
        reader.FailIf(reader.Has("MaxSourceCharsPerRender"), "MaxSourceCharsPerRender",
            $"is no longer read. {RateKey} is the rate a render may read page source at, so remove the key and restart");

        return options;
    }
}
