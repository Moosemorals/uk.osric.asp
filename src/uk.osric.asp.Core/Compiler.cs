// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT


namespace uk.osric.asp.Core;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

/// <summary>Compiles C# source code into dynamically linked libraries.</summary>
public static class Compiler {
    private static MetadataReference[] GetMetadataReferences() {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa) {
            return [.. tpa.Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p))];
        } else {
            return [];
        }
    }

    /// <summary>Compiles C# source code into a dynamically linked library.</summary>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="sink">The stream that receives the compiled assembly.</param>
    /// <returns>The result of the compilation.</returns>
    public static EmitResult Compile(string source, Stream sink) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"Dynamic_{Guid.NewGuid():N}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return compilation.Emit(sink);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(Stream image, IContext ctx) {
        AssemblyLoadContext context = new(null, isCollectible: true);

        try {
            Assembly assembly = context.LoadFromStream(image);

            if (assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(IHandler)))?.FirstOrDefault() is not Type handler) {
                throw new InvalidOperationException("No handler found in the assembly.");
            }

            if (handler.GetMethod("Handle") is not MethodInfo handle) {
                throw new InvalidOperationException("No handle method found in the handler.");
            }

            handle.Invoke(Activator.CreateInstance(handler), [ctx]);
        } finally {
            context.Unload();
        }
    }
}