// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using uk.osric.asp.Core.Compilation;

/// <summary>Compiles generated page source into an assembly image and loads compiled pages.</summary>
/// <remarks>
/// The shell depends on this interface rather than on a compiler type, so compilation can be
/// counted, timed or replaced without the shell naming Roslyn.
/// </remarks>
public interface IPageCompiler {

    /// <summary>Identifies the build that produced a compiled image.</summary>
    /// <remarks>
    /// Images carrying a different value were produced by a build with a different page contract, or
    /// under a different page API policy, and must be compiled again before they are run.
    /// </remarks>
    string RuntimeId { get; }

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated page source, not the page template.</param>
    /// <param name="libraries">
    /// Every library the page could name, or an empty list when it names none. The compiler keeps the
    /// ones the page's <c>using</c> directives reach, and their own, and compiles them into the
    /// page's assembly.
    /// </param>
    /// <param name="cancellationToken">
    /// Bounds the compile. Roslyn observes it between operations rather than inside one, so it is a
    /// coarse bound on a pathological template, not a hard one.
    /// </param>
    /// <returns>
    /// The image and the author-facing diagnostics when the page does not compile, and the keys of
    /// the libraries it used, so a caller can record what the image depends on without deriving it
    /// again.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A fault is not a page diagnostic and must reach the
    /// shell as an exception.
    /// </exception>
    CompileResult Compile(
        GeneratedSource source,
        IReadOnlyList<LibrarySource> libraries,
        CancellationToken cancellationToken = default);

    /// <summary>Checks a library's generated source, which is never run and so is never emitted.</summary>
    /// <param name="source">The generated library source, not the page template.</param>
    /// <param name="cancellationToken">
    /// Bounds the analysis. Roslyn observes it between operations rather than inside one.
    /// </param>
    /// <returns>
    /// The author-facing diagnostics and no image: a library's answer is its errors, and what it
    /// contributes to a dependent is compiled into that page.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A fault is not an author diagnostic and must reach the
    /// shell as an exception.
    /// </exception>
    CompileResult CompileLibrary(GeneratedSource source, CancellationToken cancellationToken = default);

    /// <summary>Loads a compiled image and its portable symbols so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <param name="symbols">The portable PDB bytes emitted with the image, if present.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    LoadedPage Load(byte[] image, byte[]? symbols = null);
}
