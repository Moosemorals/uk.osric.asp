// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>What a page's stored image amounts to, as the editor and the assistant describe it.</summary>
/// <remarks>
/// An image with an assembly carries warnings only, because emit either succeeds or reports an
/// error, so <see cref="CompiledWithWarnings"/> is the worst a stored and runnable page can be. The
/// state is a value rather than a string so a view cannot invent a fourth way of saying the same
/// thing; <see cref="PageImageStatus.Describe(PageImageState)"/> is the only spelling of the words.
/// </remarks>
internal enum PageImageState {

    /// <summary>No image has been built for the template yet.</summary>
    NotCompiled,

    /// <summary>The image was built by another build, so its diagnostics predate the running one.</summary>
    Stale,

    /// <summary>An image record exists and carries no assembly.</summary>
    CompileFailed,

    /// <summary>An assembly with diagnostics, which are warnings.</summary>
    CompiledWithWarnings,

    /// <summary>An assembly with nothing to report.</summary>
    Compiled,
}

/// <summary>Decides an image's state, and the words and classes it is shown with.</summary>
/// <remarks>
/// The strings reach the author in the page list, the editor's history and the assistant's tool
/// results, so they are written once here rather than in each view that needs them.
/// </remarks>
internal static class PageImageStatus {

    /// <summary>Describes a stored image's state.</summary>
    /// <param name="runtimeId">The build that made the image, or null when there is no image record.</param>
    /// <param name="hasImage">Whether the record carries an emitted assembly.</param>
    /// <param name="hasDiagnostics">Whether compiling it reported anything.</param>
    /// <param name="currentRuntimeId">
    /// The build that is running now, or null to leave staleness unjudged: a retained revision from an
    /// earlier build is history rather than stale, and only the listing and the assistant ask.
    /// </param>
    /// <returns>The state the words are chosen from.</returns>
    public static PageImageState Describe(
        string? runtimeId,
        bool hasImage,
        bool hasDiagnostics,
        string? currentRuntimeId = null) {
        if (runtimeId is null) {
            return PageImageState.NotCompiled;
        }

        // Staleness is judged on the build alone: a stale image is recompiled on its next request, so
        // whatever it recorded about the last one is out of date before it is read.
        if (currentRuntimeId is not null && !string.Equals(runtimeId, currentRuntimeId, StringComparison.Ordinal)) {
            return PageImageState.Stale;
        }

        if (!hasImage) {
            return PageImageState.CompileFailed;
        }

        return hasDiagnostics ? PageImageState.CompiledWithWarnings : PageImageState.Compiled;
    }

    /// <summary>The words a state is shown with.</summary>
    /// <param name="state">The state to describe.</param>
    /// <returns>The words, which read correctly with no stylesheet at all.</returns>
    public static string Describe(this PageImageState state) => state switch {
        PageImageState.NotCompiled => "Not compiled",
        PageImageState.Stale => "Stale",
        PageImageState.CompileFailed => "Compile failed",
        PageImageState.CompiledWithWarnings => "Compiled with warnings",
        _ => "Compiled",
    };

    /// <summary>The class a view puts on the element that carries the words.</summary>
    /// <param name="state">The state to name.</param>
    /// <returns>One class per state, so a stylesheet can mark the ones that want attention.</returns>
    public static string Class(this PageImageState state) => state switch {
        PageImageState.NotCompiled => "state-not-compiled",
        PageImageState.Stale => "state-stale",
        PageImageState.CompileFailed => "state-compile-failed",
        PageImageState.CompiledWithWarnings => "state-compiled-with-warnings",
        _ => "state-compiled",
    };
}
