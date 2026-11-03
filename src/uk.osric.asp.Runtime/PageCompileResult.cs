// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using uk.osric.asp.Core.Compilation;

/// <summary>The outcome of compiling generated page source.</summary>
/// <param name="Image">The emitted assembly, or <see langword="null"/> when compilation failed.</param>
/// <param name="Symbols">The emitted portable PDB, or <see langword="null"/> when compilation failed.</param>
/// <param name="Diagnostics">
/// The author-facing diagnostics, errors and warnings, in template space. Empty when the page
/// compiled cleanly. <see cref="PageDiagnostic"/> values rather than Roslyn types keep Roslyn out
/// of the shell.
/// </param>
public sealed record PageCompileResult(byte[]? Image, byte[]? Symbols, IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the page can run; warnings do not stop it.</summary>
    public bool Success =>
        Diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);
}
