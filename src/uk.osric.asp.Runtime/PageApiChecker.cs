// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using uk.osric.asp.Core.Compilation;

/// <summary>Checks generated page source against <see cref="PageApiPolicy"/> before it is emitted.</summary>
/// <remarks>
/// Roslyn lives here so <c>Core</c> does not have to: the walk turns symbols into plain
/// <see cref="ApiReference"/> values and the policy decides. The shape rule is separate from the API
/// list because it is about the generated source as a whole rather than any one symbol.
/// </remarks>
internal static class PageApiChecker {

    private const string ShapeMessage =
        "PAGE002: page code must stay inside a code block; a declaration block may only declare types.";
    private const string NamespaceMessage = "PAGE002: a page may not declare a namespace.";
    private const string HandlerNameMessage = "PAGE002: a page may not declare its own GeneratedPage.";
    private const string AssemblyAttributeMessage = "PAGE002: a page may not add assembly attributes.";

    /// <summary>Reports everything the page may not do.</summary>
    /// <param name="tree">The parsed generated source.</param>
    /// <param name="model">The semantic model bound to <paramref name="tree"/>.</param>
    /// <returns>The author-facing diagnostics to fail the compile with; empty when the page is acceptable.</returns>
    /// <exception cref="InvalidOperationException">
    /// The generated source itself violates the policy. That is a compiler fault, not a page error.
    /// </exception>
    public static IReadOnlyList<PageDiagnostic> Check(SyntaxTree tree, SemanticModel model) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(model);

        List<PageDiagnostic> shape = CheckShape(tree);
        if (shape.Count > 0) {
            return shape;
        }

        List<PageDiagnostic> diagnostics = [];
        List<ApiReference> used = [];
        HashSet<string> banned = new(StringComparer.Ordinal);

        foreach (SyntaxNode node in tree.GetRoot().DescendantNodes()) {
            TemplatePosition? position = TemplatePositionOf(node);

            if (FindBanned(node) is { } what) {
                if (position is null) {
                    throw CompilerFault($"PAGE001: pages may not use {what}.");
                }

                if (banned.Add($"{what}@{position.Value.Line}")) {
                    diagnostics.Add(new PageDiagnostic(
                        PageDiagnosticSeverity.Error,
                        position,
                        $"PAGE001: pages may not use {what}."));
                }

                continue;
            }

            if (!Collects(node)) {
                continue;
            }

            used.AddRange(Describe(model.GetSymbolInfo(node).Symbol, tree, position));
            used.AddRange(Describe(model.GetTypeInfo(node).Type, tree, position));
        }

        foreach (PageDiagnostic violation in PageApiPolicy.Check(used)) {
            if (violation.Position is null) {
                throw CompilerFault(violation.Message);
            }

            diagnostics.Add(violation);
        }

        return diagnostics;
    }

    // The page body is a method body. Closing the method or the type gains members here, which is
    // what stops it declaring statics, its own entry point or a module initializer. A declaration
    // block hops over that by being emitted before the handler, so the rule allows the page's own
    // type declarations — and only those — ahead of the generated class.
    private static List<PageDiagnostic> CheckShape(SyntaxTree tree) {
        if (tree.GetRoot() is not CompilationUnitSyntax unit) {
            return [Error(tree.GetRoot(), ShapeMessage)];
        }

        List<PageDiagnostic> diagnostics = [];

        // A namespace would wrap the handler, so the rest of the rule cannot be applied to it.
        foreach (MemberDeclarationSyntax member in unit.Members) {
            if (member is BaseNamespaceDeclarationSyntax @namespace) {
                diagnostics.Add(Error(@namespace, NamespaceMessage));
            }
        }

        foreach (AttributeListSyntax attribute in unit.AttributeLists) {
            diagnostics.Add(Error(attribute, AssemblyAttributeMessage));
        }

        if (diagnostics.Count > 0) {
            return diagnostics;
        }

        // The generator always emits its class last, so the handler is the only member that may
        // carry a body; everything before it has to be a declaration the page hoisted.
        if (unit.Members is not [.., ClassDeclarationSyntax handler]) {
            return [Error(unit, ShapeMessage)];
        }

        if (!string.Equals(handler.Identifier.ValueText, LoadedPage.HandlerTypeName, StringComparison.Ordinal)
            || handler.Members is not [MethodDeclarationSyntax method]
            || !string.Equals(method.Identifier.ValueText, LoadedPage.HandlerMethodName, StringComparison.Ordinal)) {
            return [Error(handler, ShapeMessage)];
        }

        for (int index = 0; index < unit.Members.Count - 1; index++) {
            MemberDeclarationSyntax declaration = unit.Members[index];
            if (declaration is BaseTypeDeclarationSyntax type
                && string.Equals(type.Identifier.ValueText, LoadedPage.HandlerTypeName, StringComparison.Ordinal)) {
                diagnostics.Add(Error(type, HandlerNameMessage));
            } else if (declaration is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)) {
                diagnostics.Add(Error(declaration, ShapeMessage));
            }
        }

        return diagnostics;
    }

    private static string? FindBanned(SyntaxNode node) {
        return node switch {
            StackAllocArrayCreationExpressionSyntax => "stackalloc",
            PointerTypeSyntax => "a pointer",
            FunctionPointerTypeSyntax => "a function pointer",
            UnsafeStatementSyntax => "unsafe code",
            IdentifierNameSyntax { Identifier.ValueText: "dynamic" } => "dynamic",
            _ => null,
        };
    }

    private static PageDiagnostic Error(SyntaxNode node, string message) {
        return new PageDiagnostic(PageDiagnosticSeverity.Error, TemplatePositionOf(node), message);
    }

    private static InvalidOperationException CompilerFault(string message) {
        return new InvalidOperationException(
            $"The generated page source violates the page API policy: {message} This is a compiler fault, not a page error.");
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

    private static IEnumerable<ApiReference> Describe(ISymbol? symbol, SyntaxTree tree, TemplatePosition? position) {
        if (symbol is null or INamespaceSymbol or ILocalSymbol or IParameterSymbol or IRangeVariableSymbol
            or ITypeParameterSymbol or IDiscardSymbol or ILabelSymbol or IErrorTypeSymbol
            or ITypeSymbol { SpecialType: SpecialType.System_Void }) {
            yield break;
        }

        if (symbol is IAliasSymbol alias) {
            foreach (ApiReference reference in Describe(alias.Target, tree, position)) {
                yield return reference;
            }

            yield break;
        }

        if (IsDeclaredIn(symbol, tree)) {
            yield break;
        }

        if (symbol is INamedTypeSymbol type) {
            if (!type.IsAnonymousType) {
                yield return new ApiReference(NamespaceOf(type), type.Name, string.Empty, position);
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

        yield return new ApiReference(NamespaceOf(container), container.Name, member, position);
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

    // GetMappedLineSpan() falls back to generated space for a node outside a #line region.
    // HasMappedPath is what makes the pair template space; without it there is no position.
    private static TemplatePosition? TemplatePositionOf(SyntaxNode node) {
        FileLinePositionSpan span = node.GetLocation().GetMappedLineSpan();
        return span.HasMappedPath
            ? new TemplatePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1)
            : null;
    }
}
