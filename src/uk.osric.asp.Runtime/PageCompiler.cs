// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Globalization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Sdk;
// The page contract has a diagnostic value of its own, so the compiler names its own type here.
using PageDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;

/// <summary>Compiles generated page source into page images and loads them for execution.</summary>
public sealed class PageCompiler : IPageCompiler {

    // Bump this when a change outside the SDK and Core invalidates stored images: the compiler
    // options, the reference set, or how a compiled handler is invoked. The SDK's module version id
    // covers the page contract; Core's covers the generated source and its diagnostics.
    private const string FormatVersion = "7";

    // Bump this whenever PageApiPolicy changes. The policy is applied when a page compiles, so an
    // image built under different rules would otherwise keep being served without ever being rechecked.
    private const int PolicyVersion = 4;

    private readonly MetadataReference[] _references;

    /// <summary>Creates a compiler for this process's runtime.</summary>
    /// <exception cref="InvalidOperationException">
    /// The runtime does not describe its platform assemblies, so no page could be compiled.
    /// </exception>
    public PageCompiler() {
        _references = PageReferenceSet.ForPages();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The SDK's module version id covers what a page binds against; Core's covers the generator, so
    /// a change that alters the generated source invalidates an image even when the contract is
    /// unchanged.
    /// </remarks>
    internal static string RuntimeId { get; } =
        $"{FormatVersion}:{PolicyVersion}"
        + $":{typeof(IHandler).Assembly.ManifestModule.ModuleVersionId:N}"
        + $":{typeof(PageDiagnostic).Assembly.ManifestModule.ModuleVersionId:N}";

    string IPageCompiler.RuntimeId => RuntimeId;

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated C# source, not the page template.</param>
    /// <param name="libraries">The compiled sources the page named, in closure order.</param>
    /// <param name="cancellationToken">Bounds the compile between Roslyn operations.</param>
    /// <returns>The image, and the author-facing diagnostics when the page does not compile.</returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself is broken. A diagnostic without a template position means the
    /// fault is in code the generator wrote, not in the page, so it must not be shown to the author.
    /// </exception>
    public CompileResult Compile(
        GeneratedSource source,
        IReadOnlyList<LibrarySource> libraries,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(libraries);

        SyntaxTree page = Parse(source);
        Dictionary<string, SyntaxTree> candidates = [with(StringComparer.Ordinal)];
        foreach (LibrarySource library in libraries) {
            ArgumentNullException.ThrowIfNull(library);
            candidates[library.Key] = Parse(library.Source);
        }

        IReadOnlyList<string> used = Closure(page, candidates);
        List<SyntaxTree> authored = [page, .. used.Select(key => candidates[key])];
        CSharpCompilation compilation = Create(authored);

        // The policy runs before emit, so a page that may not use an API never becomes an image that
        // could be stored and served. The closure's trees are authored code, so a page may call a
        // library type the way it calls its own - and every library in the closure is checked here
        // too, because that is what makes trusting its types safe.
        List<PageDiagnostic> violations = [.. PageApiChecker.Check(
            page, compilation.GetSemanticModel(page), authored)];

        foreach (string key in used) {
            SyntaxTree tree = candidates[key];
            violations.AddRange(PageApiChecker
                .CheckLibrary(tree, compilation.GetSemanticModel(tree), authored)
                .Select(diagnostic => diagnostic with { Source = key }));
        }

        if (violations.Count > 0) {
            return new CompileResult(null, null, PageDiagnostic.SortedForDisplay(violations)) {
                Libraries = used,
            };
        }

        using MemoryStream image = new();
        using MemoryStream symbols = new();
        EmitResult result = compilation.Emit(
            peStream: image,
            pdbStream: symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        List<PageDiagnostic> diagnostics = MapDiagnostics(
            result.Diagnostics,
            used.ToDictionary(key => candidates[key], key => key));

        if (!result.Success && !diagnostics.Exists(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error)) {
            throw new InvalidOperationException(
                "The generated page source did not emit an assembly and reported no error diagnostic. This is a compiler fault, not a page error.");
        }

        return new CompileResult(
            result.Success ? image.ToArray() : null,
            result.Success ? symbols.ToArray() : null,
            diagnostics) {
            Libraries = used,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A library is analysed and not emitted: there is nothing to run, and the one thing an author
    /// needs is the diagnostics, positioned in the library's own template.
    /// </remarks>
    public CompileResult CompileLibrary(GeneratedSource source, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);

        SyntaxTree tree = Parse(source);
        CSharpCompilation compilation = Create([tree]);

        IReadOnlyList<PageDiagnostic> violations = PageApiChecker.CheckLibrary(
            tree, compilation.GetSemanticModel(tree), [tree]);
        if (violations.Count > 0) {
            return new CompileResult(null, null, violations);
        }

        return new CompileResult(null, null, MapDiagnostics(compilation.GetDiagnostics(cancellationToken)));
    }

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <param name="symbols">The portable PDB emitted beside the image, when there was one.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        ArgumentNullException.ThrowIfNull(image);

        return new LoadedPage(image, symbols);
    }

    private static SyntaxTree Parse(GeneratedSource source) =>
        CSharpSyntaxTree.ParseText(source.Text);

    private CSharpCompilation Create(IReadOnlyList<SyntaxTree> trees) {
        // Both a page and a library are analysed exactly as the product's own code is, so `ctx.User`
        // is `PageUser?` in either. The generated scaffold disables nullable analysis around every
        // line it wrote itself, so a warning here can only be about the author's code.
        return CSharpCompilation.Create(
            assemblyName: $"Dynamic_{Guid.NewGuid():N}",
            syntaxTrees: trees,
            references: _references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    // A page reaches a library only through the namespaces its own using directives name, and a
    // library it pulls in may reach another the same way. The fixed point is the closure: what the
    // page is compiled with, and what its image depends on. The key order keeps the answer stable.
    private static IReadOnlyList<string> Closure(
        SyntaxTree page,
        IReadOnlyDictionary<string, SyntaxTree> candidates) {
        Dictionary<string, string> byNamespace = [with(StringComparer.Ordinal)];

        foreach ((string key, SyntaxTree tree) in candidates) {
            foreach (string declared in NamespacesOf(tree)) {
                // Two libraries claiming one namespace is a diagnostic the page will see; the first
                // in key order is the one that answers, so the closure does not depend on the order
                // the candidates arrived in.
                byNamespace.TryAdd(declared, key);
            }
        }

        HashSet<string> used = [with(StringComparer.Ordinal)];
        Queue<string> pending = new(UsingsOf(page));

        while (pending.TryDequeue(out string? name)) {
            if (!byNamespace.TryGetValue(name, out string? key) || !used.Add(key)) {
                continue;
            }

            foreach (string next in UsingsOf(candidates[key])) {
                pending.Enqueue(next);
            }
        }

        return [.. used.Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> NamespacesOf(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(FullName)
            .Distinct(StringComparer.Ordinal);

    private static string FullName(BaseNamespaceDeclarationSyntax @namespace) {
        List<string> parts = [@namespace.Name.ToString()];

        for (SyntaxNode? parent = @namespace.Parent; parent is not null; parent = parent.Parent) {
            if (parent is BaseNamespaceDeclarationSyntax outer) {
                parts.Insert(0, outer.Name.ToString());
            }
        }

        return string.Join('.', parts);
    }

    // A static using names a type rather than a namespace, so it cannot pull a library in; an alias
    // resolves to the name it aliases. `global::` is spelling, not part of the namespace.
    private static IEnumerable<string> UsingsOf(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(@using => !@using.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && @using.Name is not null)
            .Select(@using => @using.Name!.ToString() is string name && name.StartsWith("global::", StringComparison.Ordinal)
                ? name["global::".Length..]
                : @using.Name!.ToString());

    // Mapped diagnostics belong to the page. Unmapped ones belong to generated code; they are a
    // fault unless a mapped error already explains the broken parse. Unmapped warnings are dropped.
    //
    // Dropping them silently is only safe because the scaffold is nullable-oblivious: the generator
    // wraps every line it wrote in `#nullable disable`, so a warning from generated code cannot
    // happen in the first place. Making an unmapped warning fatal instead would turn a new Roslyn
    // release into a 500 on a working page; ScaffoldTests is where that promise is checked, at build
    // time rather than at request time.
    private static List<PageDiagnostic> MapDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        IReadOnlyDictionary<SyntaxTree, string>? sources = null) {
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
            string? source = sources is not null
                && diagnostic.Location.SourceTree is { } tree
                && sources.TryGetValue(tree, out string? key)
                    ? key
                    : null;
            mapped.Add(new PageDiagnostic(
                isError ? PageDiagnosticSeverity.Error : PageDiagnosticSeverity.Warning,
                new TemplatePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1),
                $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}") {
                Source = source,
            });

            hasMappedError |= isError;
        }

        if (unmappedErrors.Count > 0 && !hasMappedError) {
            string details = string.Join(" ", unmappedErrors.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
            throw new InvalidOperationException(
                $"The generated page source did not compile: {details} This is a compiler fault, not a page error.");
        }

        return mapped;
    }
}
