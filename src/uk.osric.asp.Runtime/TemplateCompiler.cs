// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using uk.osric.asp.Core;

/// <summary>Compiles C# source into page assemblies and loads them for execution.</summary>
public sealed class TemplateCompiler : ITemplateCompiler {

    // Bump this when a change outside Core invalidates stored images: the compiler options, the
    // reference set, or how a compiled handler is invoked. Core's module version id covers everything
    // in the generated source's contract: IContext, IHandler and the source generator.
    private const string FormatVersion = "2";

    // Bump this whenever PageApiPolicy tightens. The policy is applied when a page compiles, so an
    // image built under looser rules would otherwise keep being served without ever being rechecked.
    private const int PolicyVersion = 1;

    // Built once per process: re-reading and re-creating the references was a large part of the cost
    // of compiling a small page. The set itself is trimmed in CreateMetadataReferences.
    private static readonly MetadataReference[] s_metadataReferences = CreateMetadataReferences();

    /// <summary>Identifies the build and the page policy that produced a compiled image.</summary>
    public static string RuntimeId { get; } =
        $"{FormatVersion}:{PolicyVersion}:{typeof(IHandler).Assembly.ManifestModule.ModuleVersionId:N}";

    string ITemplateCompiler.RuntimeId => RuntimeId;

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated C# source, not the page template.</param>
    /// <returns>Whether the assembly was emitted, the image, and the diagnostics if it was not.</returns>
    public CompileResult Compile(string source) {
        ArgumentNullException.ThrowIfNull(source);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"Dynamic_{Guid.NewGuid():N}",
            syntaxTrees: [tree],
            references: s_metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // The policy runs before emit, so a page that may not use an API never becomes an image that
        // could be stored and served.
        IReadOnlyList<string> violations = PageApiChecker.Check(tree, compilation.GetSemanticModel(tree));
        if (violations.Count > 0) {
            return new CompileResult(false, null, violations);
        }

        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image);
        IReadOnlyList<string> diagnostics = [.. result.Diagnostics.Select(diagnostic => diagnostic.ToString())];

        return new CompileResult(result.Success, result.Success ? image.ToArray() : null, diagnostics);
    }

    /// <summary>Loads a compiled image so its handler can be run.</summary>
    /// <param name="image">The bytes an earlier <see cref="Compile"/> emitted.</param>
    /// <returns>The loaded handler; dispose it to release the assembly.</returns>
    public CompiledTemplate Load(byte[] image) {
        ArgumentNullException.ThrowIfNull(image);

        return new CompiledTemplate(image);
    }

    private static MetadataReference[] CreateMetadataReferences() {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trustedPlatformAssemblies) {
            return [];
        }

        List<string> metadataPaths = [typeof(IHandler).Assembly.Location];

        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator)) {
            if (!IsReachableFromPages(Path.GetFileNameWithoutExtension(path))) {
                continue;
            }

            metadataPaths.Add(path);
        }

        return [.. metadataPaths.Select(path => MetadataReference.CreateFromFile(path))];
    }

    // The reference set is the cheap half of the page policy: it turns "a page could call this" into
    // CS0246 before any analysis runs, so the shell, the web stack and Roslyn cannot even be named.
    // It cannot remove System.IO or System.Environment, which live in or forward from
    // System.Private.CoreLib, and it cannot stop reflection by name - PageApiPolicy and the load
    // context do that. A post-emit scan of the image's type references was considered and dropped: it
    // could only catch what the semantic walk already reports, at the cost of false rejections for
    // compiler-emitted helpers such as DefaultInterpolatedStringHandler.
    private static bool IsReachableFromPages(string assemblyName) {
        return !assemblyName.StartsWith("uk.osric.asp.", StringComparison.OrdinalIgnoreCase)
            && !assemblyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
    }
}
