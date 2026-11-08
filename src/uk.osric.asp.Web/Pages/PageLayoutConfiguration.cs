// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>The page every HTML page is wrapped in unless it says otherwise.</summary>
/// <remarks>
/// Read once at startup from <c>Pages:Layout</c>. A blank value switches wrapping off, so an
/// installation that wants today's unwrapped behaviour has a configuration answer rather than a
/// code change; the key is canonicalised so a layout is named the same way a request would name it.
/// </remarks>
internal sealed record PageLayoutConfiguration {

    /// <summary>The layout every page is wrapped in, or <see langword="null"/> when there is none.</summary>
    public string? Default { get; init; }

    /// <summary>Reads <c>Pages:Layout</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    public static PageLayoutConfiguration FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        string? configured = configuration["Pages:Layout"];

        return new PageLayoutConfiguration {
            Default = string.IsNullOrWhiteSpace(configured) ? null : PagePath.Normalize(configured).Key,
        };
    }
}
