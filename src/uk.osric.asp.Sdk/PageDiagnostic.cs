// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One problem a save found in the template, ready for the author to read.</summary>
/// <remarks>
/// <para>
/// This is the page's copy of the compiler's diagnostic: severity, a position in the template the
/// author typed, the code the logs and the editor quote, and the text. <c>Core.Compilation</c> keeps
/// its own <c>PageDiagnostic</c>, because moving it here would make the generator depend on the page
/// contract; the page's save maps between them (`pages-a-page-may-save-itself`).
/// </para>
/// <para>
/// A position is 1-based template space, and an unknown position is both members <see langword="null"/>
/// rather than <c>0</c>, so it cannot be mistaken for a real one. A diagnostic about a run of text
/// carries where the run ends as well as where it begins; a diagnostic that names one spot leaves
/// the end <see langword="null"/>, which a consumer reads as "the end is the start".
/// </para>
/// </remarks>
/// <param name="Severity">Whether the page can still be served.</param>
/// <param name="Line">The 1-based line in the template, or <see langword="null"/> when it is not known.</param>
/// <param name="Column">The 1-based column in the template, or <see langword="null"/> when it is not known.</param>
/// <param name="Code">
/// The author-facing code — <c>PAGE001</c>, <c>TEMPLATE002</c>, <c>CS1002</c> — or empty when the
/// message carries none.
/// </param>
/// <param name="Message">The text of the diagnostic, without its code, position or severity.</param>
public readonly record struct PageDiagnostic(
    PageDiagnosticSeverity Severity,
    int? Line,
    int? Column,
    string Code,
    string Message) {

    /// <summary>
    /// The 1-based line the diagnostic's extent ends on, exclusive, or <see langword="null"/> when it
    /// covers <see cref="Line"/> and nothing more.
    /// </summary>
    public int? EndLine { get; init; }

    /// <summary>
    /// The 1-based column just past the extent on <see cref="EndLine"/>, or <see langword="null"/>
    /// when the diagnostic covers <see cref="Line"/> and nothing more.
    /// </summary>
    public int? EndColumn { get; init; }

    /// <summary>
    /// The key of the page or library the diagnostic is about, or <see langword="null"/> for the page
    /// being saved.
    /// </summary>
    /// <remarks>
    /// A page that names a library compiles the library's code too, so a diagnostic can be about a
    /// file the author is not looking at. A page that shows its own diagnostics names the key when
    /// this is set and says nothing when it is not; the page whose save answered is the one with no
    /// key.
    /// </remarks>
    public string? Source { get; init; }
}
