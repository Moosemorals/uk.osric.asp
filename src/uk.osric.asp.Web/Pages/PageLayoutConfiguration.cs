// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

/// <summary>The page every HTML page with code is wrapped in unless it says otherwise.</summary>
/// <remarks>
/// Read once at startup from <c>Pages:Layout</c>. The value must be present; an explicit blank
/// switches wrapping off, so an installation that wants no wrapping at all has a configuration
/// answer rather than a code change. The key is canonicalised so a layout is named the same way a
/// request would name it.
/// </remarks>
internal sealed record PageLayoutConfiguration {

    /// <summary>The layout every page is wrapped in, or <see langword="null"/> when there is none.</summary>
    public required string? Default { get; init; }

    /// <summary>Reads <c>Pages:Layout</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">The value is missing.</exception>
    public static PageLayoutConfiguration FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Layout");
        PageLayoutConfiguration options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Layout</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Layout</c>, which the caller completes.</param>
    internal static PageLayoutConfiguration FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        string configured = reader.Value("a page key such as /_layout, or a blank string for no layout",
            allowEmpty: true);

        return new PageLayoutConfiguration {
            Default = string.IsNullOrWhiteSpace(configured) ? null : PagePath.Normalize(configured).Key,
        };
    }
}
