// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

/// <summary>Which page the host offers the editor capability to.</summary>
/// <remarks>
/// The capability is a handle on the whole store, so the key that holds it is policy and comes from
/// <c>Pages:Editor:Key</c>. A key no request could reach is refused at startup rather than left to
/// become a site with no editor.
/// </remarks>
internal sealed record PageEditorOptions {

    /// <summary>The one content key that is offered <c>IPageEditor</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Reads <c>Pages:Editor</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">The key is missing or names no page a request could reach.</exception>
    /// <summary>Reads <c>Pages:Editor</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageEditorOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Editor");
        PageEditorOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Editor</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Editor</c>, which the caller completes.</param>
    internal static PageEditorOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        string configured = reader.Text("Key", "a page key such as /pages");
        PagePath path = PagePath.Normalize(configured);

        reader.FailIf(!path.IsAddressable, "Key",
            $"is \"{configured}\", which is not an addressable page key such as /pages");

        return new PageEditorOptions { Key = path.Key };

    }
}
