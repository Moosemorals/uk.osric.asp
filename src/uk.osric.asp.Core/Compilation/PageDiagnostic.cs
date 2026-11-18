// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>One problem found in a page, ready to display.</summary>
/// <param name="Severity">Whether the page can still run.</param>
/// <param name="Position">Where in the template, or <see langword="null"/> when the position is not known.</param>
/// <param name="Message">The diagnostic code and text, without severity or position.</param>
public sealed record PageDiagnostic(
    PageDiagnosticSeverity Severity,
    TemplatePosition? Position,
    string Message) {

    /// <summary>
    /// Where the diagnostic stops, exclusive, or <see langword="null"/> when it covers
    /// <see cref="Position"/> and nothing more.
    /// </summary>
    /// <remarks>
    /// An absent end is the start: a consumer that only wants a line never meets a second kind of
    /// unknown, and a producer that knows only where the problem begins leaves this out rather than
    /// repeating the position. The end is one past the last character, as an editor's selection is,
    /// so a consumer that underlines the run needs no arithmetic.
    /// </remarks>
    public TemplatePosition? End { get; init; }

    /// <summary>
    /// The key of the page the diagnostic belongs to, or <see langword="null"/> for the page being
    /// compiled. A library compiled into a page contributes its diagnostics under its own key.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>Orders diagnostics the way the author reads them: the page first, then its libraries.</summary>
    /// <param name="diagnostics">The diagnostics to order.</param>
    /// <returns>The same diagnostics, ordered for display.</returns>
    /// <remarks>
    /// The page's own diagnostics come first, then each library's by key, and within one source a
    /// diagnostic without a position sorts last. The sort is stable, so diagnostics at the same
    /// position keep the order the compiler gave them.
    /// </remarks>
    public static IReadOnlyList<PageDiagnostic> SortedForDisplay(IEnumerable<PageDiagnostic> diagnostics) {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return [.. diagnostics
            .OrderBy(diagnostic => diagnostic.Source is null ? 0 : 1)
            .ThenBy(diagnostic => diagnostic.Source, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Position?.Line ?? int.MaxValue)
            .ThenBy(diagnostic => diagnostic.Position?.Column ?? int.MaxValue)];
    }

    /// <summary>Renders the diagnostic as one line, with its source, position and severity.</summary>
    public override string ToString() {
        string severity = Severity == PageDiagnosticSeverity.Error ? "error" : "warning";
        string? where = Position is { } start ? Describe(start) : null;
        if (Source is { } source) {
            where = where is null ? source : $"{source}, {where}";
        }

        return where is null ? $"{severity} {Message}" : $"{where}: {severity} {Message}";
    }

    // The run a diagnostic covers, in the words an author reads: one position for a point, two
    // columns for a run on one line, and both ends when the run crosses a line break.
    private string Describe(TemplatePosition start) {
        if (End is not { } end || end == start) {
            return start.ToString();
        }

        return end.Line == start.Line
            ? $"line {start.Line}, columns {start.Column}-{end.Column}"
            : $"line {start.Line}, column {start.Column} to line {end.Line}, column {end.Column}";
    }
}
