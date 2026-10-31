// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using uk.osric.asp.Core;

/// <summary>The outcome of compiling a generated template.</summary>
/// <param name="Image">The emitted assembly, or <see langword="null"/> when compilation failed.</param>
/// <param name="Diagnostics">
/// The author-facing diagnostics, errors and warnings, in template space. Empty when the page
/// compiled cleanly. <see cref="PageDiagnostic"/> values rather than Roslyn types keep Roslyn out
/// of the shell.
/// </param>
public sealed record CompileResult(byte[]? Image, IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the page can run; warnings do not stop it.</summary>
    public bool Success =>
        Diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);
}
