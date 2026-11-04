// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using uk.osric.asp.Core.Compilation;

/// <summary>One line of a diagnostics block.</summary>
/// <param name="Text">The message, unencoded: <see cref="PageLayout.Diagnostics(IReadOnlyList{DiagnosticLine}, bool)"/> encodes it.</param>
/// <param name="Severity">The severity, or null for a line that carries none, such as a stack location.</param>
/// <param name="Line">The source line the line points at, when it points at one.</param>
public readonly record struct DiagnosticLine(string Text, PageDiagnosticSeverity? Severity, int? Line) {

    /// <summary>Turns compiler diagnostics into lines, ordered by source position.</summary>
    /// <param name="diagnostics">The diagnostics to show.</param>
    /// <returns>One line per diagnostic, in the order it should be read.</returns>
    public static IReadOnlyList<DiagnosticLine> From(IReadOnlyList<PageDiagnostic> diagnostics) {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return [.. PageDiagnostic.SortedForDisplay(diagnostics)
            .Select(diagnostic => new DiagnosticLine(diagnostic.ToString(), diagnostic.Severity, diagnostic.Position?.Line))];
    }
}
