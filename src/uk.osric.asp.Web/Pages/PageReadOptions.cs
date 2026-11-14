// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page's read of the store may cost.</summary>
/// <remarks>
/// Every value comes from <c>Pages:Read</c>. The row cap is the store's limit; the source cap is
/// shared by the whole layout chain, on the same terms as the body and fetch allowances (DEC-150).
/// </remarks>
internal sealed record PageReadOptions {

    /// <summary>The largest row cap the site will run with.</summary>
    public const int MaximumRowsPerList = 10_000;

    /// <summary>The smallest source allowance the site will run with.</summary>
    public const int MinimumSourceCharsPerRender = 1024;

    /// <summary>The most rows one listing may return.</summary>
    public required int MaxRowsPerList { get; init; }

    /// <summary>How much template text one render may pull in, across every layer.</summary>
    public required int MaxSourceCharsPerRender { get; init; }

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
            MaxSourceCharsPerRender = reader.Number("MaxSourceCharsPerRender", recommended: 512 * 1024,
                minimum: MinimumSourceCharsPerRender, maximum: 16 * 1024 * 1024),
        };
        return options;
    }
}
