// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The assistant's own menu: its three views, with the one being shown marked.</summary>
/// <remarks>
/// It is the assistant's menu rather than a site capability: no page asks for it, and when the
/// assistant becomes a page this moves or is replaced with the page's own menu.
/// </remarks>
internal static class AssistantNavigation {

    /// <summary>The label no other navigation landmark may repeat.</summary>
    public const string Label = "Assistant navigation";

    /// <summary>Renders the assistant's menu for the request being answered.</summary>
    /// <param name="context">The request whose path decides the current item.</param>
    public static string Nav(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        string path = context.Request.Path.Value ?? "/assistant";
        string memory = AssistantMemoryEndpoint.Path;
        bool atMemory = At(path, memory);

        return ShellDocument.Nav(Label, [
            new("/assistant", "Conversations", At(path, "/assistant") && !atMemory),
            new(memory, "Memory", atMemory),
            // The editor is a stored page with its own document, so its menu cannot mark it here.
            new(EditorPaths.Of(context.RequestServices), "Page list", false),
        ]);
    }

    private static bool At(string path, string key) =>
        string.Equals(path, key, StringComparison.Ordinal)
        || path.StartsWith(key + "/", StringComparison.Ordinal);
}
