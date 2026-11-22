// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using Microsoft.CodeAnalysis;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Documentation;

/// <summary>Builds the reference corpus the site serves and the assistant reads.</summary>
/// <remarks>
/// Run beside an assembly build: it reads the compiler's XML documentation and the metadata it
/// describes, resolves <c>inheritdoc</c> and every <c>cref</c>, spells each declaration, and writes
/// one JSON document per assembly. The site then has no XML to massage and no XSLT to run.
/// </remarks>
internal static class Program {

    /// <summary>Runs the corpus build.</summary>
    /// <param name="args">The optional directory holding the assemblies and their XML.</param>
    public static int Main(string[] args) {
        string directory = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
        if (!Directory.Exists(directory)) {
            Console.Error.WriteLine($"reference-corpus: {directory} is not a directory");
            return 2;
        }

        List<ReferenceDocument> documents = [];
        List<(ReferenceDocument Document, SymbolCorpus Symbols)> loaded = [];
        Dictionary<string, ISymbol> symbols = [];
        Dictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries = [];
        Dictionary<string, ReferenceDeclaration> declarations = [];

        foreach (ReferenceAssemblyInfo info in ReferenceAssemblyInfo.All) {
            string xml = Path.Combine(directory, info.AssemblyName + ".xml");
            string dll = Path.Combine(directory, info.AssemblyName + ".dll");
            if (!File.Exists(xml) || !File.Exists(dll)) {
                Console.Error.WriteLine($"reference-corpus: this build does not carry {info.AssemblyName}");
                continue;
            }

            ReferenceDocument document = Normalise(info, xml);
            SymbolCorpus assembly = SymbolCorpus.Read(dll, directory);
            Console.WriteLine($"{info.Slug}: {assembly.All.Count} symbols");
            documents.Add(document);
            loaded.Add((document, assembly));
            foreach ((string id, ISymbol symbol) in assembly.All) {
                _ = symbols.TryAdd(id, symbol);
            }

            foreach ((string id, ReferenceEntry entry) in document.Entries) {
                entries[id] = (document, entry);
            }
        }

        SymbolInheritance.Resolve(documents, symbols, entries, Console.WriteLine);
        Crefs.Annotate(documents, entries);

        // Declarations are annotated last: a type named in a signature may live in an assembly read
        // after the one that names it, so every entry has to be known before the link is chosen.
        foreach ((ReferenceDocument document, SymbolCorpus assembly) in loaded) {
            foreach ((string id, ReferenceDeclaration declaration) in Declarations.Annotate(
                document,
                assembly,
                symbol => Resolve(symbol, entries))) {
                declarations[id] = declaration;
            }
        }

        foreach (ReferenceDocument document in documents) {
            ReferenceCorpus corpus = ReferenceModelBuilder.Build(document, declarations);
            string path = Path.Combine(directory, "reference." + document.Info.Slug + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(corpus, JsonSerialization.Options));
            Console.WriteLine($"{document.Info.Slug}: {corpus.TypeCount} types, {corpus.EntryCount} entries -> {Path.GetFileName(path)}");
        }

        return 0;
    }

    private static string? Resolve(
        ISymbol symbol,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) {
        if (symbol is not INamedTypeSymbol type
            || type.SpecialType != SpecialType.None
            || type.IsAnonymousType
            || type.TypeKind == TypeKind.Error) {
            return null;
        }

        string? id = type.OriginalDefinition.GetDocumentationCommentId();
        if (id is null) {
            return null;
        }

        return entries.TryGetValue(id, out (ReferenceDocument Document, ReferenceEntry Entry) target)
            ? ReferenceUrls.Page(target.Document, target.Entry)
            : ReferenceDisplay.LearnUrl(id);
    }

    private static ReferenceDocument Normalise(ReferenceAssemblyInfo info, string path) {
        using FileStream stream = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return ReferenceDocument.Build(info, XDocument.Load(reader));
    }
}
