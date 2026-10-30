// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

/// <summary>The outcome of compiling a generated template.</summary>
/// <param name="Success">Whether the assembly was emitted.</param>
/// <param name="Diagnostics">
/// The compiler's error messages, already formatted for display. Empty when <paramref name="Success"/>
/// is <see langword="true"/>. Strings rather than Roslyn types keep Roslyn out of the shell.
/// </param>
public record CompileResult(bool Success, IReadOnlyList<string> Diagnostics);
