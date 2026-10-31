// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

/// <summary>Compiles template source and loads compiled pages.</summary>
/// <remarks>
/// The shell depends on this interface rather than on a compiler type, so compilation can be
/// counted, timed or replaced without the shell naming Roslyn.
/// </remarks>
public interface ITemplateCompiler {

    /// <summary>Identifies the build that produced a compiled image.</summary>
    /// <remarks>
    /// Images carrying a different value were produced by a build with a different page contract, or
    /// under a different page API policy, and must be compiled again before they are run.
    /// </remarks>
    string RuntimeId { get; }

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated C# source, not the page template.</param>
    /// <returns>The image, and the author-facing diagnostics when the page does not compile.</returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A fault is not a page diagnostic and must reach the
    /// shell as an exception.
    /// </exception>
    CompileResult Compile(string source);

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    CompiledTemplate Load(byte[] image);
}
