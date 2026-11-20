// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

using System.Text.Json.Nodes;
using uk.osric.asp.Core.Compilation;

/// <summary>Turns the compiler's diagnostics into the protocol's values.</summary>
/// <remarks>
/// A diagnostic is already positioned in template space, which is the author's own line and column,
/// so the only work here is the protocol's two conventions: positions are 0-based, and the end of a
/// range is exclusive, which is what <see cref="PageDiagnostic.End"/> already is.
/// </remarks>
internal static class LspDiagnostics {

    /// <summary>The name a diagnostic is attributed to in the editor.</summary>
    public const string SourceName = "asp";

    private const int ErrorSeverity = 1;
    private const int WarningSeverity = 2;

    /// <summary>Renders one diagnostic as the protocol's object.</summary>
    /// <param name="diagnostic">The compiler's diagnostic.</param>
    /// <returns>The diagnostic, positioned and with its code split out of the message.</returns>
    public static JsonObject ToJson(PageDiagnostic diagnostic) {
        ArgumentNullException.ThrowIfNull(diagnostic);

        TemplatePosition start = diagnostic.Position ?? new TemplatePosition(1, 1);
        TemplatePosition end = diagnostic.End ?? new TemplatePosition(start.Line, start.Column + 1);
        (string code, string message) = Split(diagnostic.Message);

        JsonObject item = new() {
            ["range"] = new JsonObject {
                ["start"] = Position(start),
                ["end"] = Position(end),
            },
            ["severity"] = diagnostic.Severity == PageDiagnosticSeverity.Error ? ErrorSeverity : WarningSeverity,
            ["source"] = SourceName,
            ["message"] = message,
        };

        if (code.Length > 0) {
            item["code"] = code;
        }

        return item;
    }

    private static JsonObject Position(TemplatePosition position) => new() {
        // The template counts from one and the protocol from zero.
        ["line"] = position.Line - 1,
        ["character"] = position.Column - 1,
    };

    // Every author-facing message is "{code}: {text}", the one form every producer writes. The code
    // is a field of its own in the protocol, so it is lifted out rather than shown twice.
    private static (string Code, string Message) Split(string message) {
        int separator = message.IndexOf(": ", StringComparison.Ordinal);

        return separator > 0
            ? (message[..separator], message[(separator + 2)..])
            : (string.Empty, message);
    }
}
