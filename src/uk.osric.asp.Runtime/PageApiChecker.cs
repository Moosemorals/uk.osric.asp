// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using uk.osric.asp.Core;

/// <summary>Checks generated page source against <see cref="PageApiPolicy"/> before it is emitted.</summary>
/// <remarks>
/// Roslyn lives here so <c>Core</c> does not have to: the walk turns symbols into plain
/// <see cref="ApiReference"/> values and the policy decides. The shape rule is separate from the API
/// list because it is about the generated source as a whole rather than any one symbol.
/// </remarks>
internal static class PageApiChecker {

    private const string HandlerTypeName = "GeneratedPage";
    private const string HandlerMethodName = "Handle";

    /// <summary>Reports everything the page may not do.</summary>
    /// <param name="tree">The parsed generated source.</param>
    /// <param name="model">The semantic model bound to <paramref name="tree"/>.</param>
    /// <returns>The diagnostics to fail the compile with; empty when the page is acceptable.</returns>
    public static IReadOnlyList<string> Check(SyntaxTree tree, SemanticModel model) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(model);

        List<string> shape = CheckShape(tree);
        if (shape.Count > 0) {
            return shape;
        }

        List<ApiReference> used = [];
        HashSet<string> banned = new(StringComparer.Ordinal);

        foreach (SyntaxNode node in tree.GetRoot().DescendantNodes()) {
            int line = PageLine(node);

            if (FindBanned(node, line) is { } violation) {
                banned.Add(violation);
                continue;
            }

            if (!Collects(node)) {
                continue;
            }

            used.AddRange(Describe(model.GetSymbolInfo(node).Symbol, tree, line));
            used.AddRange(Describe(model.GetTypeInfo(node).Type, tree, line));
        }

        List<string> diagnostics = [.. banned];
        diagnostics.AddRange(PageApiPolicy.Check(used));

        return diagnostics;
    }

    // The page body is a method body. A page that closes the method or the type gains members here,
    // which is what stops it declaring statics, its own entry point or a module initializer.
    private static List<string> CheckShape(SyntaxTree tree) {
        const string Message =
            "PAGE002: page code must stay inside a code block; a page may not declare types or members.";

        List<string> diagnostics = [];

        if (tree.GetRoot() is not CompilationUnitSyntax unit) {
            diagnostics.Add(Message);
            return diagnostics;
        }

        if (unit.Members is not [ClassDeclarationSyntax type]
            || !string.Equals(type.Identifier.ValueText, HandlerTypeName, StringComparison.Ordinal)) {
            diagnostics.Add(Message);
            return diagnostics;
        }

        if (type.Members is not [MethodDeclarationSyntax method]
            || !string.Equals(method.Identifier.ValueText, HandlerMethodName, StringComparison.Ordinal)) {
            diagnostics.Add(Message);
        }

        return diagnostics;
    }

    private static string? FindBanned(SyntaxNode node, int line) {
        string? what = node switch {
            StackAllocArrayCreationExpressionSyntax => "stackalloc",
            PointerTypeSyntax => "a pointer",
            FunctionPointerTypeSyntax => "a function pointer",
            UnsafeStatementSyntax => "unsafe code",
            IdentifierNameSyntax { Identifier.ValueText: "dynamic" } => "dynamic",
            _ => null,
        };

        return what is null ? null : Banned(what, line);
    }

    private static string Banned(string what, int line) {
        return line > 0
            ? $"PAGE001: pages may not use {what} (line {line})."
            : $"PAGE001: pages may not use {what}.";
    }

    // Only these node kinds can introduce an API a page did not already have; declarations and
    // predefined types are left out so `void` and the like do not need allow-listing.
    private static bool Collects(SyntaxNode node) {
        return node is IdentifierNameSyntax
            or GenericNameSyntax
            or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ObjectCreationExpressionSyntax
            or BaseTypeSyntax
            or AttributeSyntax
            or TypeOfExpressionSyntax
            or CastExpressionSyntax;
    }

    private static IEnumerable<ApiReference> Describe(ISymbol? symbol, SyntaxTree tree, int line) {
        if (symbol is null or INamespaceSymbol or ILocalSymbol or IParameterSymbol or IRangeVariableSymbol
            or ITypeParameterSymbol or IDiscardSymbol or ILabelSymbol or IErrorTypeSymbol
            or ITypeSymbol { SpecialType: SpecialType.System_Void }) {
            yield break;
        }

        if (symbol is IAliasSymbol alias) {
            foreach (ApiReference reference in Describe(alias.Target, tree, line)) {
                yield return reference;
            }

            yield break;
        }

        if (IsDeclaredIn(symbol, tree)) {
            yield break;
        }

        if (symbol is INamedTypeSymbol type) {
            if (!type.IsAnonymousType) {
                yield return new ApiReference(NamespaceOf(type), type.Name, string.Empty, line);
            }

            yield break;
        }

        if (symbol.ContainingType is not { } container) {
            yield break;
        }

        // Using a constructor is using the type, so the diagnostic names the type rather than ".ctor".
        string member = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor }
            ? string.Empty
            : symbol.Name;

        yield return new ApiReference(NamespaceOf(container), container.Name, member, line);
    }

    // A symbol declared in this tree is page code - the handler, a local function, a lambda, an
    // anonymous type - and needs no permission. Walking the container chain covers all of them.
    private static bool IsDeclaredIn(ISymbol symbol, SyntaxTree tree) {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingSymbol) {
            foreach (SyntaxReference reference in current.DeclaringSyntaxReferences) {
                if (reference.SyntaxTree == tree) {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NamespaceOf(INamedTypeSymbol type) {
        INamespaceSymbol? containing = type.OriginalDefinition.ContainingNamespace;
        return containing is null or { IsGlobalNamespace: true } ? string.Empty : containing.ToDisplayString();
    }

    // The generated source keeps the page's newlines, so this mapping is exact, not approximate.
    private static int PageLine(SyntaxNode node) {
        int generatedLine = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        int pageLine = generatedLine - TemplateSourceGenerator.PageCodeFirstLine + 1;
        return pageLine > 0 ? pageLine : 0;
    }
}
