// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The words a stored image's state is described with, wherever it is shown.</summary>
/// <remarks>
/// The strings reach the author in the editor's history and in the assistant's tool results, so they
/// are written once here rather than in each view that needs them.
/// </remarks>
internal static class PageImageStatus {

    /// <summary>No image has been built for the template yet.</summary>
    public const string NotCompiled = "Not compiled";

    /// <summary>An image was built and the compilation failed.</summary>
    public const string CompileFailed = "Compile failed";

    /// <summary>An image was built, with diagnostics worth reading.</summary>
    public const string CompiledWithDiagnostics = "Compiled with diagnostics";

    /// <summary>An image was built cleanly.</summary>
    public const string Compiled = "Compiled";

    /// <summary>Describes an image's state.</summary>
    /// <param name="runtimeId">The build that made the image, or null when there is no image record.</param>
    /// <param name="hasImage">Whether the record carries an emitted assembly.</param>
    /// <param name="hasDiagnostics">Whether compiling it reported anything.</param>
    public static string Describe(string? runtimeId, bool hasImage, bool hasDiagnostics) {
        if (runtimeId is null) {
            return NotCompiled;
        }

        if (!hasImage) {
            return CompileFailed;
        }

        return hasDiagnostics ? CompiledWithDiagnostics : Compiled;
    }
}
