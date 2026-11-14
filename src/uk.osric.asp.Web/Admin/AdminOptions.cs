// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Web.Configuration;

/// <summary>The values the admin routes read from configuration.</summary>
/// <remarks>
/// Read once at startup, so a missing setting stops the site rather than a page the owner opens.
/// </remarks>
internal sealed record AdminOptions {

    /// <summary>The changelist file, relative to the assembly unless it is absolute.</summary>
    public required string Changelist { get; init; }

    /// <summary>Reads <c>Admin</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static AdminOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Admin");
        AdminOptions options = new() {
            Changelist = reader.Text("Changelist", "a file name such as changes.txt"),
        };
        reader.Complete();
        return options;
    }
}
