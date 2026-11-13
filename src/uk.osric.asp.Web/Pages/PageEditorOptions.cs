// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Which page the host offers the editor capability to.</summary>
/// <remarks>
/// The capability is a handle on the whole store, so the key that holds it is policy and lives with
/// the other policy values rather than in a constant that needs a rebuild to change. A value that
/// names no page falls back to the default, so a typo cannot leave a release with no editor.
/// </remarks>
internal sealed record PageEditorOptions {

    /// <summary>The editor's key when none is configured.</summary>
    public const string DefaultKey = "/pages";

    /// <summary>The one content key that is offered <c>IPageEditor</c>.</summary>
    public string Key { get; init; } = DefaultKey;

    /// <summary>Reads <c>Pages:Editor</c>, falling back to the default when the key names no page.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never a key no request could reach.</returns>
    public static PageEditorOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        string? configured = configuration.GetSection("Pages:Editor").GetValue<string>("Key");
        if (string.IsNullOrWhiteSpace(configured)) {
            return new PageEditorOptions();
        }

        PagePath path = PagePath.Normalize(configured);

        return new PageEditorOptions {
            Key = path.IsAddressable ? path.Key : DefaultKey,
        };
    }
}
