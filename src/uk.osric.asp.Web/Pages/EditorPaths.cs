// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Reads the editor's key, the one policy every link to the editor is built from.</summary>
/// <remarks>
/// The key is configuration (<c>Pages:Editor:Key</c>) rather than a constant, so a shell view that
/// hard-codes it sends the owner to a page that is no longer the editor. The value is required, so a
/// request without one is a wiring mistake rather than a second spelling of the key.
/// </remarks>
internal static class EditorPaths {

    /// <summary>The editor's key for this request.</summary>
    /// <param name="services">The request's services.</param>
    /// <exception cref="InvalidOperationException">The request has no editor key.</exception>
    public static string Of(IServiceProvider? services) =>
        services?.GetService<PageEditorOptions>()?.Key
        ?? throw new InvalidOperationException("The request has no editor key; the host registers Pages:Editor:Key.");
}
