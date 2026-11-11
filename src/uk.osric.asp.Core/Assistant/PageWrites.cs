// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;
using uk.osric.asp.Core.Pages;

/// <summary>Reads which page a write or edit tool reported it changed.</summary>
/// <remarks>
/// Only the stored tool result is consulted, because that is what the run left behind: a call that was
/// refused, or one whose arguments only named a page a later check rejected, changed nothing. The
/// result envelope is the assistant's own (<c>{"ok":…,"data":{"path":…},"error":…}</c>), and a result
/// too large to store is replaced by a failure envelope, so a success envelope always parses.
/// </remarks>
public static class PageWrites {

    /// <summary>The page a successful write or edit reported.</summary>
    /// <param name="changesPages">Whether the tool changes a stored page, as the tool itself says.</param>
    /// <param name="resultJson">The stored result of that call.</param>
    /// <returns>The canonical key of the changed page, or <see langword="null"/> when the call was not
    /// a page write, did not succeed, or named a key no request can address.</returns>
    public static PagePath? Written(bool changesPages, string? resultJson) {
        if (!changesPages || string.IsNullOrEmpty(resultJson)) {
            return null;
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(resultJson);
        } catch (JsonException) {
            return null;
        }

        using (document) {
            string? reported = ReportedPath(document.RootElement);
            if (reported is null) {
                return null;
            }

            PagePath path = PagePath.Normalize(reported);
            return path.IsAddressable ? path : null;
        }
    }

    private static string? ReportedPath(JsonElement result) {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("ok", out JsonElement ok)
            || ok.ValueKind != JsonValueKind.True
            || !result.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("path", out JsonElement path)
            || path.ValueKind != JsonValueKind.String) {
            return null;
        }

        return path.GetString();
    }
}
