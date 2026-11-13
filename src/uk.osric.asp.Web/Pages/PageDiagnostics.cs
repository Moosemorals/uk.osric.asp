// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using CoreDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using CoreSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;
using SdkDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using SdkSeverity = uk.osric.asp.Sdk.PageDiagnosticSeverity;

/// <summary>Turns the compiler's diagnostics into the values a page may name.</summary>
/// <remarks>
/// Every author-facing message is "{code}: {text}" in the one form every producer writes — Roslyn's
/// "CS1002: ; expected" and the host's own "PAGE001: ..." alike — so the code is the prefix of the
/// message rather than a field of its own (DEC-193). One mapping serves a self-save and the editor.
/// </remarks>
internal static class PageDiagnostics {

    /// <summary>Maps the compiler's answer, ordered by template position.</summary>
    /// <param name="diagnostics">The compiler's diagnostics.</param>
    public static IReadOnlyList<SdkDiagnostic> ToSdk(IEnumerable<CoreDiagnostic> diagnostics) =>
        [.. CoreDiagnostic.SortedForDisplay(diagnostics).Select(ToSdk)];

    /// <summary>Maps one of the compiler's diagnostics.</summary>
    /// <param name="diagnostic">The compiler's diagnostic.</param>
    public static SdkDiagnostic ToSdk(CoreDiagnostic diagnostic) {
        string code = string.Empty;
        string message = diagnostic.Message;
        int separator = message.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0) {
            code = message[..separator];
            message = message[(separator + 2)..];
        }

        return new SdkDiagnostic(
            diagnostic.Severity == CoreSeverity.Error ? SdkSeverity.Error : SdkSeverity.Warning,
            diagnostic.Position?.Line,
            diagnostic.Position?.Column,
            code,
            message);
    }
}
