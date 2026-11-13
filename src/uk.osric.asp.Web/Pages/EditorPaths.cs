// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Reads the editor's key, the one policy every link to the editor is built from.</summary>
/// <remarks>
/// The key is configuration (<c>Pages:Editor:Key</c>) rather than a constant, so a shell view that
/// hard-codes it sends the owner to a page that is no longer the editor. A request that carries no
/// services — a unit test's hand-built <c>HttpContext</c> — falls back to the shipped default rather
/// than to a second spelling of it.
/// </remarks>
internal static class EditorPaths {

    /// <summary>The editor's key for this request.</summary>
    /// <param name="services">The request's services, or <see langword="null"/> when it has none.</param>
    public static string Of(IServiceProvider? services) =>
        services?.GetService<PageEditorOptions>()?.Key ?? PageEditorOptions.DefaultKey;
}
