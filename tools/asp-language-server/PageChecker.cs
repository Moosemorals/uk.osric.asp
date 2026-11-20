// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>Answers what a template compiles to.</summary>
/// <remarks>
/// The server is not a second checker: the site's own generation, policy and compiler answer, and this
/// interface exists so the protocol suite can drive the server without Roslyn when it is proving the
/// protocol rather than the compiler.
/// </remarks>
internal interface IPageChecker {

    /// <summary>Checks one template.</summary>
    /// <param name="template">The template as its author wrote it.</param>
    /// <returns>Its diagnostics, ordered for display; empty when it is clean.</returns>
    IReadOnlyList<PageDiagnostic> Check(string template);
}

/// <summary>The site's own compile tier, over the repository's seed libraries.</summary>
/// <remarks>
/// It follows a save's front half exactly: generate the source, generate each library in the shape a
/// library is compiled in, compile, and keep the shape check's warnings beside the compiler's. The
/// libraries are the repository's seed files rather than a page store, because an editor has no
/// database; a seed library a page does not reach is never compiled into it, because the compiler
/// keeps only what the page's own <c>using</c> directives reach.
/// </remarks>
internal sealed class CompilerPageChecker(IPageCompiler compiler, IReadOnlyList<LibrarySource> libraries) : IPageChecker {

    /// <inheritdoc/>
    public IReadOnlyList<PageDiagnostic> Check(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);
        IReadOnlyList<PageDiagnostic> shape = TemplateShapeCheck.Check(template);

        // Nothing to compile, so the shape check is the whole answer, exactly as a save leaves it.
        if (generated.Kind is PageKind.Text) {
            return PageDiagnostic.SortedForDisplay(shape);
        }

        // A source that did not generate never reaches the compiler; a declaration-only source is
        // generated again in the shape a library is compiled in, as a save does it.
        IReadOnlyList<PageDiagnostic> compiled;
        if (generated.Source is not { } source) {
            compiled = generated.Errors;
        } else if (generated.Kind is PageKind.Library) {
            GeneratedSource library = TemplateSourceGenerator.GenerateLibrary(template).Source!;
            compiled = compiler.CompileWorker(library, libraries).Diagnostics;
        } else {
            compiled = compiler.Compile(source, libraries).Diagnostics;
        }

        return PageDiagnostic.SortedForDisplay([.. shape, .. compiled]);
    }
}
