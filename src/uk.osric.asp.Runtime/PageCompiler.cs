// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using uk.osric.asp.Core;
using uk.osric.asp.Sdk;

/// <summary>Compiles generated page source into page images and loads them for execution.</summary>
public sealed class PageCompiler : IPageCompiler {

    // Bump this when a change outside the SDK and Core invalidates stored images: the compiler
    // options, the reference set, or how a compiled handler is invoked. The SDK's module version id
    // covers the page contract; Core's covers the generated source and its diagnostics.
    private const string FormatVersion = "3";

    // Bump this whenever PageApiPolicy tightens. The policy is applied when a page compiles, so an
    // image built under looser rules would otherwise keep being served without ever being rechecked.
    private const int PolicyVersion = 1;

    private readonly MetadataReference[] _references;

    /// <summary>Creates a compiler for this process's runtime.</summary>
    /// <exception cref="InvalidOperationException">
    /// The runtime does not describe its platform assemblies, so no page could be compiled.
    /// </exception>
    public PageCompiler() {
        _references = PageReferenceSet.ForPages();
    }

    /// <summary>Identifies the build and the page policy that produced a compiled image.</summary>
    /// <remarks>
    /// Both module version ids are in the value. The SDK's covers what a page binds against; Core's
    /// covers the generator, so a change that alters the generated source invalidates an image even
    /// when the contract is unchanged.
    /// </remarks>
    internal static string RuntimeId { get; } =
        $"{FormatVersion}:{PolicyVersion}"
        + $":{typeof(IHandler).Assembly.ManifestModule.ModuleVersionId:N}"
        + $":{typeof(PageDiagnostic).Assembly.ManifestModule.ModuleVersionId:N}";

    string IPageCompiler.RuntimeId => RuntimeId;

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated C# source, not the page template.</param>
    /// <param name="cancellationToken">Bounds the compile between Roslyn operations.</param>
    /// <returns>The image, and the author-facing diagnostics when the page does not compile.</returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A diagnostic without a template position means the
    /// fault is in code the generator wrote, not in the page, so it must not be shown to the author.
    /// </exception>
    public PageCompileResult Compile(string source, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"Dynamic_{Guid.NewGuid():N}",
            syntaxTrees: [tree],
            references: _references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // The policy runs before emit, so a page that may not use an API never becomes an image that
        // could be stored and served.
        IReadOnlyList<PageDiagnostic> violations = PageApiChecker.Check(tree, compilation.GetSemanticModel(tree));
        if (violations.Count > 0) {
            return new PageCompileResult(null, violations);
        }

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image, cancellationToken: cancellationToken);
        List<PageDiagnostic> diagnostics = MapDiagnostics(result.Diagnostics);

        if (!result.Success && !diagnostics.Exists(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error)) {
            throw new InvalidOperationException(
                "The generated page source did not emit an assembly and reported no error diagnostic. This is a compiler fault, not a page error.");
        }

        return new PageCompileResult(result.Success ? image.ToArray() : null, diagnostics);
    }

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    public CompiledPage Load(byte[] image) {
        ArgumentNullException.ThrowIfNull(image);

        return new CompiledPage(image);
    }

    // Mapped diagnostics belong to the page. Unmapped ones belong to generated code; they are a
    // fault unless a mapped error already explains the broken parse. Unmapped warnings are dropped.
    private static List<PageDiagnostic> MapDiagnostics(IEnumerable<Diagnostic> diagnostics) {
        List<PageDiagnostic> mapped = [];
        List<Diagnostic> unmappedErrors = [];
        bool hasMappedError = false;

        foreach (Diagnostic diagnostic in diagnostics) {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) {
                continue;
            }

            FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
            if (!span.HasMappedPath) {
                if (diagnostic.Severity == DiagnosticSeverity.Error) {
                    unmappedErrors.Add(diagnostic);
                }

                continue;
            }

            bool isError = diagnostic.Severity == DiagnosticSeverity.Error;
            mapped.Add(new PageDiagnostic(
                isError ? PageDiagnosticSeverity.Error : PageDiagnosticSeverity.Warning,
                new TemplatePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1),
                $"{diagnostic.Id}: {diagnostic.GetMessage()}"));

            hasMappedError |= isError;
        }

        if (unmappedErrors.Count > 0 && !hasMappedError) {
            string details = string.Join(" ", unmappedErrors.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}"));
            throw new InvalidOperationException(
                $"The generated page source did not compile: {details} This is a compiler fault, not a page error.");
        }

        return mapped;
    }
}
