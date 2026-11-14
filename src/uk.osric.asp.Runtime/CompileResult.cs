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
// The arrays are the compiler's emitted blobs, handed over once and never mutated; wrapping them in
// a read-only view would copy the image or invent a type the emitter does not produce.
#pragma warning disable CA1819
public sealed record CompileResult(byte[]? Image, byte[]? Symbols, IReadOnlyList<PageDiagnostic> Diagnostics) {
#pragma warning restore CA1819

    /// <summary>Whether the page can run; warnings do not stop it.</summary>
    public bool IsSuccess =>
        Diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);

    /// <summary>
    /// The keys of the libraries the page was compiled with, in ordinal order; empty when it named
    /// none. An image that carries this list was built against exactly these libraries.
    /// </summary>
    public IReadOnlyList<string> Libraries { get; init; } = [];
}
