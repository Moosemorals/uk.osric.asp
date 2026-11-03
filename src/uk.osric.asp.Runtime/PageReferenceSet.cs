// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Microsoft.CodeAnalysis;
using uk.osric.asp.Core;

/// <summary>The assemblies a page is compiled against.</summary>
/// <remarks>
/// Built once per process: re-reading and re-creating the references was a large part of the cost of
/// compiling a small page. A runtime that cannot supply the set has no working reference set at all,
/// so it fails here rather than in the first page that is compiled.
/// </remarks>
internal static class PageReferenceSet {

    private const string TrustedPlatformAssembliesKey = "TRUSTED_PLATFORM_ASSEMBLIES";

    private const string CoreLibraryName = "System.Private.CoreLib";

    private static readonly Lock s_gate = new();

    private static MetadataReference[]? s_references;

    /// <summary>The reference set the runtime provides, trimmed to what a page may name.</summary>
    /// <returns>The references to compile generated page source against.</returns>
    /// <exception cref="InvalidOperationException">
    /// The runtime did not describe its platform assemblies, or described none that pages can use.
    /// </exception>
    internal static MetadataReference[] ForRuntime() {
        lock (s_gate) {
            // Only a successful set is kept, so a failure to read one assembly is not remembered for
            // the life of the process.
            return s_references ??= Create();
        }
    }

    /// <summary>The platform assembly paths a page may reference.</summary>
    /// <param name="trustedPlatformAssemblies">
    /// The runtime's <c>TRUSTED_PLATFORM_ASSEMBLIES</c> value, as read from <see cref="AppContext.GetData"/>.
    /// </param>
    /// <returns>The paths that remain, in the order the runtime listed them.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value is missing, empty, or names no assembly every page is built on.
    /// </exception>
    internal static IReadOnlyList<string> ReferencedPaths(string? trustedPlatformAssemblies) {
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies)) {
            throw new InvalidOperationException(
                $"{TrustedPlatformAssembliesKey} is missing or empty, so pages would be compiled against no assemblies "
                + "and every page would fail with errors about types that are not referenced. A framework-dependent "
                + "deployment provides the value; a single-file or trimmed deployment does not.");
        }

        List<string> paths = [];
        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            if (IsReachableFromPages(Path.GetFileNameWithoutExtension(path))) {
                paths.Add(path);
            }
        }

        // Without the core library the set is present but useless: that is exactly the set that
        // reports "The type 'object' is defined in an assembly that is not referenced".
        if (!paths.Exists(IsCoreLibrary)) {
            throw new InvalidOperationException(
                $"{TrustedPlatformAssembliesKey} names no {CoreLibraryName}, so pages could not compile even object. "
                + "The runtime described its platform assemblies, but not the one the base class library lives in.");
        }

        return paths;
    }

    private static MetadataReference[] Create() {
        List<string> paths = [typeof(IHandler).Assembly.Location];
        paths.AddRange(ReferencedPaths(AppContext.GetData(TrustedPlatformAssembliesKey) as string));

        return [.. paths.Select(path => MetadataReference.CreateFromFile(path))];
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

    private static bool IsCoreLibrary(string path) {
        return string.Equals(Path.GetFileNameWithoutExtension(path), CoreLibraryName, StringComparison.OrdinalIgnoreCase);
    }
}
