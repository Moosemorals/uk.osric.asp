// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
// The compilation names its own diagnostic type, as the compiler does.
using PageDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;

/// <summary>Reads the tables a source declares out of the symbols a compile already holds.</summary>
/// <remarks>
/// <para>
/// The declaration is part of the revision: a `<c>&lt;%! %&gt;</c>` type carrying
/// <see cref="PageTableAttribute"/>, extracted from the syntax tree the compiler was given, so the
/// schema needs no second parse and is judged when the page is compiled.
/// </para>
/// <para>
/// The extractor answers the questions only symbols can settle — what type a property has, and whether
/// a declaration is something the store can hold at all. The limits a declaration obeys are
/// configuration, so they are checked where the schema is applied, by <see cref="PageSchema.Validate"/>.
/// </para>
/// </remarks>
internal static class PageSchemaExtractor {

    private const string PageTableAttributeName = "uk.osric.asp.Sdk.PageTableAttribute";

    /// <summary>Extracts one source's declaration.</summary>
    /// <param name="tree">The source's own tree, never a library's: a library owns its tables.</param>
    /// <param name="compilation">The compilation the tree belongs to.</param>
    /// <returns>The schema, and the problems that make it one the store cannot hold.</returns>
    public static (PageSchema Schema, List<PageDiagnostic> Problems) Extract(SyntaxTree tree, CSharpCompilation compilation) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(compilation);

        List<PageDiagnostic> problems = [];
        INamedTypeSymbol? attribute = compilation.GetTypeByMetadataName(PageTableAttributeName);
        if (attribute is null) {
            return (PageSchema.Empty, problems);
        }


        SemanticModel model = compilation.GetSemanticModel(tree);
        List<PageTable> tables = [];

        foreach (TypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()) {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol) {
                continue;
            }

            AttributeData? applied = symbol.GetAttributes()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));
            if (applied is null) {
                continue;
            }

            if (Read(applied) is not { } declaration_) {
                problems.Add(Problem(symbol,
                    $"PAGE011: [{PageTableAttributeName}] needs the table's name, as [PageTable(\"notes\")]."));
                continue;
            }

            PageTable? table = ReadTable(symbol, declaration_, problems);
            if (table is not null) {
                tables.Add(table);
            }
        }

        return (new PageSchema(tables), problems);
    }

    private sealed record Declaration(
        string Name,
        StoreTableSubject Subject,
        StoreTableLifetime Lifetime,
        bool LifetimeGiven,
        string? Search);

    private static Declaration? Read(AttributeData applied) {
        if (applied.ConstructorArguments.Length != 1 || applied.ConstructorArguments[0].Value is not string name) {
            return null;
        }

        StoreTableSubject subject = StoreTableSubject.Page;
        StoreTableLifetime lifetime = StoreTableLifetime.Page;
        bool lifetimeGiven = false;
        string? search = null;

        foreach (KeyValuePair<string, TypedConstant> named in applied.NamedArguments) {
            switch (named.Key) {
                case nameof(PageTableAttribute.Subject):
                    subject = named.Value.Value is int subjectValue && subjectValue == (int)StoreSubject.Visitor
                        ? StoreTableSubject.Visitor
                        : StoreTableSubject.Page;
                    break;
                case nameof(PageTableAttribute.Lifetime):
                    // NextRead and Default are values of one enum because the contract has one enum;
                    // neither is a table's, so a declaration that names one is refused below.
                    lifetimeGiven = true;
                    lifetime = named.Value.Value switch {
                        int kind when kind == (int)StoreLifetimeKind.Page => StoreTableLifetime.Page,
                        int kind when kind == (int)StoreLifetimeKind.Session => StoreTableLifetime.Session,
                        int kind when kind == (int)StoreLifetimeKind.Browser => StoreTableLifetime.Browser,
                        int kind when kind == (int)StoreLifetimeKind.Person => StoreTableLifetime.Person,
                        _ => (StoreTableLifetime)(-1),
                    };
                    break;
                case nameof(PageTableAttribute.Search):
                    search = named.Value.Value as string;
                    break;
                default:
                    break;
            }
        }

        // A lifetime the source did not name is the subject's own: a page's table follows the page,
        // and one visitor's table follows the person.
        if (!lifetimeGiven) {
            lifetime = subject is StoreTableSubject.Visitor ? StoreTableLifetime.Person : StoreTableLifetime.Page;
        }

        return new Declaration(name, subject, lifetime, lifetimeGiven, search);
    }

    private static PageTable? ReadTable(
        INamedTypeSymbol symbol,
        Declaration declaration,
        List<PageDiagnostic> problems) {
        if (!Enum.IsDefined(declaration.Lifetime)) {
            problems.Add(Problem(symbol,
                "PAGE012: a declared table follows the page, the person, the session or the browser; Default and NextRead are not a table's lifetimes."));
            return null;
        }

        if (declaration.Subject is StoreTableSubject.Page && declaration.LifetimeGiven && declaration.Lifetime is not StoreTableLifetime.Page) {
            problems.Add(Problem(symbol,
                $"PAGE012: \"{declaration.Name}\" is the page's table, so its rows die with the page; a table that follows something else is Subject = StoreSubject.Visitor."));
            return null;
        }

        if (declaration.Subject is StoreTableSubject.Visitor && declaration.Lifetime is StoreTableLifetime.Page) {
            problems.Add(Problem(symbol,
                $"PAGE012: \"{declaration.Name}\" holds one visitor's rows, so its lifetime is Person, Session or Browser."));
            return null;
        }

        List<PageColumn> columns = [];
        bool refused = false;
        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>()) {
            if (property.IsStatic || property.DeclaredAccessibility is not Accessibility.Public) {
                continue;
            }

            if (property.SetMethod is null) {
                problems.Add(Problem(symbol,
                    $"PAGE011: the column \"{property.Name}\" on \"{declaration.Name}\" needs a setter, because a row is read into it."));
                refused = true;
                continue;
            }

            if (TypeOf(property.Type) is not { } type) {
                problems.Add(Problem(symbol,
                    $"PAGE011: the column \"{property.Name}\" on \"{declaration.Name}\" is a {property.Type.ToDisplayString()}; a column is a string, a long, a double, a bool or a DateTimeOffset, and only a string may be nullable."));
                refused = true;
                continue;
            }

            columns.Add(new PageColumn(property.Name, type.Type, type.Nullable));
        }

        return refused ? null : new PageTable(declaration.Name, declaration.Subject, declaration.Lifetime, columns, declaration.Search);
    }

    // Only a value type that SQLite has a storage class for, and only a string may be null: a
    // nullable number would need a second column to say "absent" from "zero".
    private static (PageColumnType Type, bool Nullable)? TypeOf(ITypeSymbol type) {
        if (type.SpecialType is SpecialType.System_String) {
            return (PageColumnType.Text, type.NullableAnnotation is NullableAnnotation.Annotated);
        }

        if (type.NullableAnnotation is NullableAnnotation.Annotated || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }) {
            return null;
        }

        return type.SpecialType switch {
            SpecialType.System_Int64 => (PageColumnType.WholeNumber, false),
            SpecialType.System_Double => (PageColumnType.Number, false),
            SpecialType.System_Boolean => (PageColumnType.Flag, false),
            // By name rather than by symbol: a full reference set describes DateTimeOffset more than
            // once, and GetTypeByMetadataName refuses an ambiguous answer.
            _ => IsTimestamp(type) ? (PageColumnType.Timestamp, false) : null,
        };
    }

    private static bool IsTimestamp(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "DateTimeOffset", ContainingNamespace.Name: "System" };

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
}
