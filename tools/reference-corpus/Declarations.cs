// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using Microsoft.CodeAnalysis;
using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>Spells a symbol as the declaration a reader wants, with its tokens classified.</summary>
/// <remarks>
/// Roslyn already knows the words: accessibility, modifiers, the return type, the parameter names and
/// whether the type is a class, a record, an interface or an enum. The display format is the one
/// place that spelling lives, and the parts it returns are what the page colours.
/// </remarks>
internal static class Declarations {

    private static readonly SymbolDisplayFormat s_type = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat s_member = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static ReferenceDeclaration ForType(INamedTypeSymbol type, Func<ISymbol, string?> resolve) {
        ReferenceDeclaration display = FromParts(type.ToDisplayParts(s_type), type.Name, resolve);
        List<ReferenceToken> tokens = [];
        if (Access(type) is { } access) {
            tokens.Add(new ReferenceToken("keyword", access));
            tokens.Add(new ReferenceToken("text", " "));
        }

        foreach (string modifier in TypeModifiers(type)) {
            tokens.Add(new ReferenceToken("keyword", modifier));
            tokens.Add(new ReferenceToken("text", " "));
        }

        tokens.AddRange(display.Tokens);
        return new ReferenceDeclaration(string.Concat(tokens.Select(token => token.Text)), tokens);
    }

    // SymbolDisplay gives a type its `readonly` and `ref`, but not the three modifiers that change
    // what the type is; a reader who sees `class` where the source says `static class` is misled.
    private static IEnumerable<string> TypeModifiers(INamedTypeSymbol type) {
        if (type.TypeKind != TypeKind.Class) {
            yield break;
        }

        if (type.IsStatic) {
            yield return "static";
        } else if (type.IsAbstract) {
            yield return "abstract";
        } else if (type.IsSealed) {
            yield return "sealed";
        }
    }

    // Type symbols do not show their accessibility through SymbolDisplay, though their modifiers do;
    // the word is prepended here so a type says `public class Program` like a member says `public void M()`.
    private static string? Access(ISymbol symbol) => symbol.DeclaredAccessibility switch {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => null,
    };

    /// <summary>The declaration of one type or member.</summary>
    /// <param name="symbol">The symbol to spell.</param>
    /// <param name="resolve">The page a named type links to, or null when it has none.</param>
    public static ReferenceDeclaration For(ISymbol symbol, Func<ISymbol, string?> resolve) {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(resolve);

        return symbol switch {
            INamedTypeSymbol type => ForType(type, resolve),
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } field =>
                new(field.Name, [new ReferenceToken("member", field.Name)]),
            IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol =>
                WithAccessibility(symbol, FromParts(symbol.ToDisplayParts(s_member), symbol.Name, resolve)),
            _ => new(symbol.Name, [new ReferenceToken("text", symbol.Name)]),
        };
    }

    // Roslyn omits `public` from an interface member because the source does not have to write it.
    // A reader still wants the access word the member actually has, so it is prepended when the
    // display does not already carry it. An explicit interface implementation has no C# access
    // modifier to show, so it is left as the display spells it.
    private static ReferenceDeclaration WithAccessibility(ISymbol symbol, ReferenceDeclaration declaration) {
        if (Access(symbol) is not { } access
            || declaration.Text.StartsWith(access + " ", StringComparison.Ordinal)
            || symbol is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
            or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }
            or IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 }) {
            return declaration;
        }

        List<ReferenceToken> tokens = [
            new ReferenceToken("keyword", access),
            new ReferenceToken("text", " "),
            .. declaration.Tokens,
        ];

        return new ReferenceDeclaration(string.Concat(tokens.Select(token => token.Text)), tokens);
    }

    /// <summary>The word for a type's kind: <c>class</c>, <c>record struct</c>, and so on.</summary>
    /// <param name="symbol">The type to classify.</param>
    public static string Kind(INamedTypeSymbol symbol) => symbol switch {
        { TypeKind: TypeKind.Interface } => "interface",
        { TypeKind: TypeKind.Enum } => "enum",
        { TypeKind: TypeKind.Delegate } => "delegate",
        { TypeKind: TypeKind.Struct, IsRecord: true } => "record struct",
        { TypeKind: TypeKind.Struct } => "struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Class } => "class",
        _ => "type",
    };

    /// <summary>Writes each declaration and type kind onto the normalised tree.</summary>
    /// <param name="document">The document to annotate in place.</param>
    /// <param name="symbols">The assembly's symbols, by documentation id.</param>
    /// <param name="resolve">The page a named type links to, or null when it has none.</param>
    /// <returns>Every declaration it wrote, by documentation id.</returns>
    public static IReadOnlyDictionary<string, ReferenceDeclaration> Annotate(
        ReferenceDocument document,
        SymbolCorpus symbols,
        Func<ISymbol, string?> resolve) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(resolve);

        Dictionary<string, ReferenceDeclaration> declarations = [];
        foreach (XElement type in document.Content.Descendants("type")) {
            if (symbols.Find((string?)type.Attribute("id") ?? string.Empty) is INamedTypeSymbol symbol) {
                ReferenceDeclaration declaration = For(symbol, resolve);
                type.SetAttributeValue("declaration", declaration.Text);
                type.SetAttributeValue("type-kind", Kind(symbol));
                declarations[type.Attribute("id")!.Value] = declaration;
            }
        }

        foreach (XElement member in document.Content.Descendants("member")) {
            if (symbols.Find((string?)member.Attribute("id") ?? string.Empty) is { } symbol) {
                ReferenceDeclaration declaration = For(symbol, resolve);
                member.SetAttributeValue("declaration", declaration.Text);
                declarations[member.Attribute("id")!.Value] = declaration;
            }
        }

        return declarations;
    }

    private static ReferenceDeclaration FromParts(
        IEnumerable<SymbolDisplayPart> parts,
        string fallback,
        Func<ISymbol, string?> resolve) {
        List<ReferenceToken> tokens = [];
        foreach (SymbolDisplayPart part in parts) {
            string text = part.ToString();
            if (text.Length == 0) {
                continue;
            }

            string? href = part.Symbol is { } symbol ? resolve(symbol) : null;
            tokens.Add(new ReferenceToken(Class(part.Kind), text, href));
        }

        return tokens.Count == 0
            ? new ReferenceDeclaration(fallback, [new ReferenceToken("member", fallback)])
            : new ReferenceDeclaration(string.Concat(tokens.Select(token => token.Text)), tokens);
    }

    private static string Class(SymbolDisplayPartKind kind) => kind switch {
        SymbolDisplayPartKind.Keyword => "keyword",
        SymbolDisplayPartKind.ClassName => "type",
        SymbolDisplayPartKind.InterfaceName => "type",
        SymbolDisplayPartKind.StructName => "type",
        SymbolDisplayPartKind.EnumName => "type",
        SymbolDisplayPartKind.DelegateName => "type",
        SymbolDisplayPartKind.RecordClassName => "type",
        SymbolDisplayPartKind.RecordStructName => "type",
        SymbolDisplayPartKind.NamespaceName => "namespace",
        SymbolDisplayPartKind.MethodName => "member",
        SymbolDisplayPartKind.ExtensionMethodName => "member",
        SymbolDisplayPartKind.PropertyName => "member",
        SymbolDisplayPartKind.FieldName => "member",
        SymbolDisplayPartKind.EnumMemberName => "member",
        SymbolDisplayPartKind.EventName => "member",
        SymbolDisplayPartKind.ParameterName => "param",
        SymbolDisplayPartKind.TypeParameterName => "typeparam",
        SymbolDisplayPartKind.Operator => "punctuation",
        SymbolDisplayPartKind.Punctuation => "punctuation",
        SymbolDisplayPartKind.NumericLiteral => "literal",
        SymbolDisplayPartKind.StringLiteral => "literal",
        _ => "text",
    };
}
