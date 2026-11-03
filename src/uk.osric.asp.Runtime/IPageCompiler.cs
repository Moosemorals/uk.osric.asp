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
    /// <param name="cancellationToken">
    /// Bounds the compile. Roslyn observes it between operations rather than inside one, so it is a
    /// coarse bound on a pathological template, not a hard one.
    /// </param>
    /// <returns>The image, and the author-facing diagnostics when the page does not compile.</returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A fault is not a page diagnostic and must reach the
    /// shell as an exception.
    /// </exception>
    CompileResult Compile(string source, CancellationToken cancellationToken = default);

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    CompiledTemplate Load(byte[] image);
}
