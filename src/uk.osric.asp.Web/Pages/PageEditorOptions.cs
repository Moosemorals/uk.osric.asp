// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

/// <summary>Where the editor is served, and the key every link to it is built from.</summary>
/// <remarks>
/// The capability is offered to every page now, so this key decides nothing about authority: it says
/// where the editor lives, which is what <c>ctx.EditorPath</c>, the shell's remedy links and every
/// <c>?edit=</c> URL read. A key no request could reach is still refused at startup rather than left to
/// become a site with no editor.
/// </remarks>
internal sealed record PageEditorOptions {

    /// <summary>The page key where the editor is served.</summary>
    public required string Key { get; init; }

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
