// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Bounds what one page's read of the store may cost.</summary>
/// <remarks>
/// Every cap is clamped to a safe minimum, so a configuration mistake cannot switch a cap off. The
/// row cap is the store's limit; the source cap is shared by the whole layout chain, on the same
/// terms as the body and fetch allowances (DEC-150).
/// </remarks>
internal sealed record PageReadOptions {

    /// <summary>The most rows one listing returns, when none is configured.</summary>
    public const int DefaultMaxRowsPerList = 500;

    /// <summary>The most source characters one render may read, when none is configured.</summary>
    public const int DefaultMaxSourceCharsPerRender = 512 * 1024;

    /// <summary>The smallest source allowance a configuration may choose.</summary>
    public const int MinimumSourceCharsPerRender = 1024;

    /// <summary>The largest row cap a configuration may choose.</summary>
    public const int MaximumRowsPerList = 10000;

    /// <summary>The most rows one listing may return.</summary>
    public int MaxRowsPerList { get; init; } = DefaultMaxRowsPerList;

    /// <summary>How much template text one render may pull in, across every layer.</summary>
    public int MaxSourceCharsPerRender { get; init; } = DefaultMaxSourceCharsPerRender;

    /// <summary>Reads <c>Pages:Read</c>, falling back to the defaults when it is missing or silly.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never values that would disable a cap.</returns>
    public static PageReadOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Read");

        return new PageReadOptions {
            MaxRowsPerList = Math.Clamp(
                section.GetValue("MaxRowsPerList", DefaultMaxRowsPerList), 1, MaximumRowsPerList),
            MaxSourceCharsPerRender = Math.Max(
                MinimumSourceCharsPerRender,
                section.GetValue("MaxSourceCharsPerRender", DefaultMaxSourceCharsPerRender)),
        };
    }
}
