// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a checked source turned out to be, judged from its own text.</summary>
/// <remarks>
/// <para>
/// This is the page's copy of the host's verdict, because the page contract references nothing: a
/// check reports what generation discovered, so a caller that expected a page and was given a
/// library learns it in the same answer rather than by a second read.
/// </para>
/// <para>
/// <see cref="Text"/> is a source with no code at all; <see cref="Page"/> renders; <see cref="Library"/>
/// is only something another page compiles against; and <see cref="Worker"/> is a program a page
/// starts. Generation can only tell that a source holds declarations and nothing to render, so it
/// answers <see cref="Library"/>; whether those declarations are a worker is a question about types,
/// which only the compiler answers.
/// </para>
/// </remarks>
public enum PageKind {

    /// <summary>No code at all, so the source is served as it stands.</summary>
    Text,

    /// <summary>Code that renders: it is compiled to a handler and wrapped in the site layout.</summary>
    Page,

    /// <summary>Only declarations: compiled into the pages that name it, and never served.</summary>
    Library,

    /// <summary>Declarations that declare one entry point: started by a page, and never served.</summary>
    Worker,
}
