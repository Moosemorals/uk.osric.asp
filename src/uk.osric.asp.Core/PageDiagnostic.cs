// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>One problem found in a page, ready to display.</summary>
/// <param name="Severity">Whether the page can still run.</param>
/// <param name="Position">Where in the template, or <see langword="null"/> when the position is not known.</param>
/// <param name="Message">The diagnostic code and text, without severity or position.</param>
public sealed record PageDiagnostic(
    PageDiagnosticSeverity Severity,
    TemplatePosition? Position,
    string Message) {

    /// <summary>Orders diagnostics the way the author reads the template: by line, then column.</summary>
    /// <param name="diagnostics">The diagnostics to order.</param>
    /// <returns>The same diagnostics, ordered for display.</returns>
    /// <remarks>
    /// A diagnostic without a position cannot be placed in the template, so it sorts last. The sort
    /// is stable, so diagnostics at the same position keep the order the compiler gave them.
    /// </remarks>
    public static IReadOnlyList<PageDiagnostic> SortedForDisplay(IEnumerable<PageDiagnostic> diagnostics) {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return [.. diagnostics
            .OrderBy(diagnostic => diagnostic.Position?.Line ?? int.MaxValue)
            .ThenBy(diagnostic => diagnostic.Position?.Column ?? int.MaxValue)];
    }

    /// <summary>Renders the diagnostic as one line, with its position and severity.</summary>
    public override string ToString() {
        string position = Position is { } value ? $"{value}: " : string.Empty;
        string severity = Severity == PageDiagnosticSeverity.Error ? "error" : "warning";
        return $"{position}{severity} {Message}";
    }
}
