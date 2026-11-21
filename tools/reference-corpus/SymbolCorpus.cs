// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>The symbols one assembly declares, by the documentation id the compiler emits for them.</summary>
/// <remarks>
/// The XML file carries the prose but not the signature: no accessibility, no return type, no word
/// for whether a type is a class or a record. The metadata carries all of it, and Roslyn is the
/// reader that already knows how to spell an id, so the ids here are the compiler's own.
/// </remarks>
internal sealed class SymbolCorpus {

    private readonly Dictionary<string, ISymbol> _symbols;

    private SymbolCorpus(Dictionary<string, ISymbol> symbols) => _symbols = symbols;

    /// <summary>Reads one assembly's symbols, and the ids the documentation file uses.</summary>
    /// <param name="assemblyPath">The assembly to read.</param>
    /// <param name="directory">The folder its dependencies sit in.</param>
    public static SymbolCorpus Read(string assemblyPath, string directory) {
        MetadataReference target = MetadataReference.CreateFromFile(assemblyPath);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "reference-corpus",
            syntaxTrees: null,
            references: [target, .. References(directory, assemblyPath)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Dictionary<string, ISymbol> symbols = [];
        if (compilation.GetAssemblyOrModuleSymbol(target) is IAssemblySymbol assembly) {
            Visit(assembly.GlobalNamespace, symbols);
        } else {
            Console.Error.WriteLine($"reference-corpus: no symbols read from {assemblyPath}");
        }

        return new SymbolCorpus(symbols);
    }

    /// <summary>One symbol by its documentation id.</summary>
    /// <param name="id">The id the XML file names.</param>
    public ISymbol? Find(string id) => _symbols.GetValueOrDefault(id);

    /// <summary>Every symbol the assembly declares, by documentation id.</summary>
    public IReadOnlyDictionary<string, ISymbol> All => _symbols;

    private static void Visit(INamespaceSymbol space, Dictionary<string, ISymbol> symbols) {
        foreach (INamedTypeSymbol type in space.GetTypeMembers()) {
            Visit(type, symbols);
        }

        foreach (INamespaceSymbol child in space.GetNamespaceMembers()) {
            Visit(child, symbols);
        }
    }

    private static void Visit(INamedTypeSymbol type, Dictionary<string, ISymbol> symbols) {
        Add(type, symbols);

        foreach (INamedTypeSymbol nested in type.GetTypeMembers()) {
            Visit(nested, symbols);
        }

        foreach (ISymbol member in type.GetMembers()) {
            if (member.IsImplicitlyDeclared
                || member is IMethodSymbol {
                    MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                    or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise
                }) {
                continue;
            }

            Add(member, symbols);
        }
    }

    private static void Add(ISymbol symbol, Dictionary<string, ISymbol> symbols) {
        if (symbol.GetDocumentationCommentId() is { Length: > 0 } id) {
            _ = symbols.TryAdd(id, symbol);
        }
    }

    private static IEnumerable<MetadataReference> References(string directory, string assemblyPath) {
        HashSet<string> seen = [];
        string target = Path.GetFullPath(assemblyPath);
        foreach (string path in CandidateFiles(directory)) {
            string name = Path.GetFileName(path);
            if (string.Equals(Path.GetFullPath(path), target, StringComparison.Ordinal) || !seen.Add(name)) {
                continue;
            }

            MetadataReference? reference = TryCreate(path);
            if (reference is not null) {
                yield return reference;
            }
        }
    }

    private static IEnumerable<string> CandidateFiles(string directory) {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted) {
            foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
                yield return path;
            }
        }

        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;
        string shared = Path.GetDirectoryName(runtime) ?? string.Empty;
        string aspNet = Path.Combine(shared, "Microsoft.AspNetCore.App");
        if (Directory.Exists(aspNet)) {
            string? version = Directory.GetDirectories(aspNet).OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();
            if (version is not null) {
                foreach (string path in Directory.EnumerateFiles(version, "*.dll")) {
                    yield return path;
                }
            }
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.dll")) {
            yield return path;
        }
    }

    private static MetadataReference? TryCreate(string path) {
        try {
            return MetadataReference.CreateFromFile(path);
        } catch (Exception exception) when (exception is BadImageFormatException or FileNotFoundException or IOException) {
            return null;
        }
    }
}
