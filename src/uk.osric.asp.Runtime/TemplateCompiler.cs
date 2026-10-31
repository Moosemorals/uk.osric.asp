// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using uk.osric.asp.Core;

/// <summary>Compiles C# source into page assemblies and loads them for execution.</summary>
public sealed class TemplateCompiler : ITemplateCompiler {

    // Bump the prefix when a change outside Core invalidates stored images: the compiler options,
    // the reference set, or how a compiled handler is invoked. Core's module version id covers
    // everything in the generated source's contract: IContext, IHandler and the source generator.
    private const string FormatVersion = "1";

    // Built once per process. A compilation needs a reference for every platform assembly, and
    // re-reading and re-creating them was a large part of the cost of compiling a small page.
    private static readonly MetadataReference[] s_metadataReferences = CreateMetadataReferences();

    /// <summary>Identifies the build that produced a compiled image.</summary>
    public static string RuntimeId { get; } =
        $"{FormatVersion}:{typeof(IHandler).Assembly.ManifestModule.ModuleVersionId:N}";

    string ITemplateCompiler.RuntimeId => RuntimeId;

    /// <summary>Compiles generated C# source into an assembly image.</summary>
    /// <param name="source">The generated C# source, not the page template.</param>
    /// <returns>Whether the assembly was emitted, the image, and the diagnostics if it was not.</returns>
    public CompileResult Compile(string source) {
        ArgumentNullException.ThrowIfNull(source);

        using MemoryStream image = new();
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"Dynamic_{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: s_metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

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
        List<string> metadataPaths = [typeof(IHandler).Assembly.Location];
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies) {
            metadataPaths.AddRange(trustedPlatformAssemblies.Split(Path.PathSeparator));
        } else {
            return [];
        }

        return [.. metadataPaths.Select(path => MetadataReference.CreateFromFile(path))];
    }
}
