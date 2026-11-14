// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>What a page's stored image amounts to, as the editor and the assistant describe it.</summary>
/// <remarks>
/// An image with an assembly carries warnings only, because emit either succeeds or reports an
/// error, so <see cref="CompiledWithWarnings"/> is the worst a stored and runnable page can be. The
/// state is a value rather than a string so a view cannot invent another way of saying the same
/// thing; <see cref="PageImageStatus.Describe(PageImageState)"/> is the only spelling of the words.
/// <para>
/// A library is checked rather than emitted, so it never has an assembly and its diagnostics are its
/// only signal. The listing can therefore say that a library's check reported something, but not
/// whether it was a warning or an error; the editor shows the diagnostics themselves.
/// </para>
/// </remarks>
internal enum PageImageState {

    /// <summary>The template has no code, so it is stored and served as source.</summary>
    StoredAsSource,

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

    /// <summary>A library whose check is current and reported nothing.</summary>
    Library,

    /// <summary>A library whose check reported a warning or an error.</summary>
    LibraryWithIssues,
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
    /// <param name="kind">What the template is for, or null when it has never been judged.</param>
    /// <returns>The state the words are chosen from.</returns>
    /// <remarks>
    /// A library never carries an assembly, so <paramref name="hasImage"/> plays no part in its
    /// state: the check is the record, and its diagnostics are what it reported.
    /// </remarks>
    public static PageImageState Describe(
        string? runtimeId,
        bool hasImage,
        bool hasDiagnostics,
        string? currentRuntimeId = null,
        PageKind? kind = null) {
        if (kind is PageKind.Text) {
            return PageImageState.StoredAsSource;
        }

        if (runtimeId is null) {
            return PageImageState.NotCompiled;
        }

        // Staleness is judged on the build alone: a stale image is recompiled on its next request, so
        // whatever it recorded about the last one is out of date before it is read.
        if (currentRuntimeId is not null && !string.Equals(runtimeId, currentRuntimeId, StringComparison.Ordinal)) {
            return PageImageState.Stale;
        }

        if (kind is PageKind.Library) {
            return hasDiagnostics ? PageImageState.LibraryWithIssues : PageImageState.Library;
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
        PageImageState.StoredAsSource => "Stored as source",
        PageImageState.NotCompiled => "Not compiled",
        PageImageState.Stale => "Stale",
        PageImageState.CompileFailed => "Compile failed",
        PageImageState.CompiledWithWarnings => "Compiled with warnings",
        PageImageState.Library => "Library",
        PageImageState.LibraryWithIssues => "Library with issues",
        _ => "Compiled",
    };

    /// <summary>The class a view puts on the element that carries the words.</summary>
    /// <param name="state">The state to name.</param>
    /// <returns>One class per state, so a stylesheet can mark the ones that want attention.</returns>
    public static string Class(this PageImageState state) => state switch {
        PageImageState.StoredAsSource => "state-source",
        PageImageState.NotCompiled => "state-not-compiled",
        PageImageState.Stale => "state-stale",
        PageImageState.CompileFailed => "state-compile-failed",
        PageImageState.CompiledWithWarnings => "state-compiled-with-warnings",
        PageImageState.Library => "state-library",
        PageImageState.LibraryWithIssues => "state-library-with-issues",
        _ => "state-compiled",
    };
}
