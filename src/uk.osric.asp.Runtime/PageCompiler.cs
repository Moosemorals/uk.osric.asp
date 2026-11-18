// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Globalization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
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
    private const int PolicyVersion = 5;

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

        Prepared prepared = Prepare(source, libraries, asDeclarations: false);
        if (prepared.Violations.Count > 0) {
            return new CompileResult(null, null, PageDiagnostic.SortedForDisplay(prepared.Violations)) {
                Libraries = prepared.Used,
            };
        }

        return Emit(prepared, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A declaration-only source is analysed first: a worker is emitted, so the runner has something
    /// to load, while a library is not, because there is nothing to run and the one thing an author
    /// needs is the diagnostics.
    /// </remarks>
    public CompileResult CompileWorker(
        GeneratedSource source,
        IReadOnlyList<LibrarySource> libraries,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(libraries);

        Prepared prepared = Prepare(source, libraries, asDeclarations: true);
        PageKind kind = Refine(prepared, out List<PageDiagnostic> shape);

        List<PageDiagnostic> problems = [.. prepared.Violations, .. shape];
        if (problems.Count > 0) {
            return new CompileResult(null, null, PageDiagnostic.SortedForDisplay(problems)) {
                Libraries = prepared.Used,
                Kind = kind,
            };
        }

        if (kind is PageKind.Library) {
            return new CompileResult(null, null, MapDiagnostics(
                prepared.Compilation.GetDiagnostics(cancellationToken),
                prepared.SourcesByTree)) {
                Libraries = prepared.Used,
                Kind = kind,
            };
        }

        CompileResult emitted = Emit(prepared, cancellationToken);

        return emitted with { Kind = kind };
    }

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <param name="symbols">The portable PDB emitted beside the image, when there was one.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        ArgumentNullException.ThrowIfNull(image);

        return new LoadedPage(image, symbols);
    }

    /// <summary>Loads a compiled worker so its entry point can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="CompileWorker"/> emitted.</param>
    /// <param name="symbols">The portable PDB emitted beside the image, when there was one.</param>
    /// <returns>The loaded worker; dispose it to release the assembly.</returns>
    public LoadedWorker LoadWorker(byte[] image, byte[]? symbols = null) {
        ArgumentNullException.ThrowIfNull(image);

        return new LoadedWorker(image, symbols);
    }

    // Everything a compile needs before it can answer: the trees, the compilation, the closure and
    // the policy's verdict. A page and a declaration-only source differ only in what they do with it.
    private sealed record Prepared(
        CSharpCompilation Compilation,
        SyntaxTree Source,
        IReadOnlyList<string> Used,
        List<PageDiagnostic> Violations,
        IReadOnlyDictionary<SyntaxTree, string> SourcesByTree);

    // A declaration-only unit is held to the library rules: it has no handler, so declaring a
    // namespace is exactly what a worker does and a page may not.
    private Prepared Prepare(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, bool asDeclarations) {
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
        List<PageDiagnostic> violations = asDeclarations
            ? [.. PageApiChecker.CheckLibrary(page, compilation.GetSemanticModel(page), authored)]
            : [.. PageApiChecker.Check(page, compilation.GetSemanticModel(page), authored)];

        foreach (string key in used) {
            SyntaxTree tree = candidates[key];
            violations.AddRange(PageApiChecker
                .CheckLibrary(tree, compilation.GetSemanticModel(tree), authored)
                .Select(diagnostic => diagnostic with { Source = key }));
        }

        return new Prepared(
            compilation,
            page,
            used,
            violations,
            used.ToDictionary(key => candidates[key], key => key));
    }

    private static CompileResult Emit(Prepared prepared, CancellationToken cancellationToken) {
        using MemoryStream image = new();
        using MemoryStream symbols = new();
        EmitResult result = prepared.Compilation.Emit(
            peStream: image,
            pdbStream: symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            cancellationToken: cancellationToken);
        List<PageDiagnostic> diagnostics = MapDiagnostics(result.Diagnostics, prepared.SourcesByTree);

        if (!result.Success && !diagnostics.Exists(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error)) {
            throw new InvalidOperationException(
                "The generated page source did not emit an assembly and reported no error diagnostic. This is a compiler fault, not a page error.");
        }

        return new CompileResult(
            result.Success ? image.ToArray() : null,
            result.Success ? symbols.ToArray() : null,
            diagnostics) {
            Libraries = prepared.Used,
        };
    }

    // Generation can only say "declarations and nothing to render". Which declarations they are is a
    // question about types, so the worker verdict is made here, once, from the compiled symbols.
    private static PageKind Refine(Prepared prepared, out List<PageDiagnostic> problems) {
        problems = [];

        INamedTypeSymbol? contract = prepared.Compilation.GetTypeByMetadataName(typeof(IWorker).FullName!);
        if (contract is null) {
            return PageKind.Library;
        }

        SemanticModel model = prepared.Compilation.GetSemanticModel(prepared.Source);
        List<INamedTypeSymbol> entryPoints = [];
        foreach (TypeDeclarationSyntax declaration in prepared.Source.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()) {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol
                || symbol.ContainingType is not null
                || symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct)
                || symbol.IsAbstract
                || !symbol.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default)) {
                continue;
            }

            entryPoints.Add(symbol);
        }

        if (entryPoints.Count == 0) {
            return PageKind.Library;
        }

        if (entryPoints.Count > 1) {
            string names = string.Join(", ", entryPoints.Select(symbol => symbol.Name));
            foreach (INamedTypeSymbol extra in entryPoints.Skip(1)) {
                problems.Add(Problem(extra,
                    $"PAGE006: a worker declares exactly one IWorker entry point, but this source declares {entryPoints.Count}: {names}."));
            }

            return PageKind.Worker;
        }

        INamedTypeSymbol entryPoint = entryPoints[0];
        if (entryPoint.DeclaredAccessibility is not Accessibility.Public) {
            problems.Add(Problem(entryPoint, $"PAGE006: the worker entry point {entryPoint.Name} must be public."));
        }

        // A struct is always constructible with no arguments; a class needs a constructor that takes none.
        bool constructible = entryPoint.TypeKind is TypeKind.Struct
            || entryPoint.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0);
        if (!constructible) {
            problems.Add(Problem(entryPoint, $"PAGE006: the worker entry point {entryPoint.Name} needs a parameterless constructor."));
        }

        return PageKind.Worker;
    }

    private static PageDiagnostic Problem(INamedTypeSymbol symbol, string message) {
        TemplatePosition? position = null;
        foreach (Location location in symbol.Locations) {
            if (location.IsInSource && location.GetMappedLineSpan() is { HasMappedPath: true } span) {
                position = new TemplatePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
                break;
            }
        }

        return new PageDiagnostic(PageDiagnosticSeverity.Error, position, message);
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
            TemplatePosition start = new(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
            TemplatePosition end = new(span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
            mapped.Add(new PageDiagnostic(
                isError ? PageDiagnosticSeverity.Error : PageDiagnosticSeverity.Warning,
                start,
                $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}") {
                // An end at or before the start is a diagnostic about a point: a zero-width token, or
                // a span whose end mapped round a line the generator wrote. The start stays exact.
                End = end.Line > start.Line || (end.Line == start.Line && end.Column > start.Column)
                    ? end
                    : null,
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
