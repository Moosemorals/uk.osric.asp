// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>What a stored template is for, decided by its source rather than its key.</summary>
/// <remarks>
/// <para>
/// The verdict comes from generation and is stored beside the source, the same way "has code" was:
/// a page is judged by what it contains, so nothing is configured and editing the source can change
/// the kind.
/// </para>
/// <para>
/// <see cref="Text"/> is a complete representation and its source is the response;
/// <see cref="Page"/> renders; <see cref="Library"/> is only something another page compiles
/// against, so it is never served and never given a render; <see cref="Worker"/> is a program a page
/// starts, which is also never served.
/// </para>
/// <para>
/// Generation can only tell that a template has declarations and nothing to render, so it answers
/// <see cref="Library"/>. Whether those declarations are a worker is a question about types, which
/// only the compiler can answer: a library that declares a public entry point is refined to
/// <see cref="Worker"/> when it compiles.
/// </para>
/// </remarks>
public enum PageKind {

    /// <summary>No C# at all, so the stored source is the response byte for byte.</summary>
    Text,

    /// <summary>C# that renders: it is compiled to a handler and wrapped in the site layout.</summary>
    Page,

    /// <summary>Only declarations: compiled into the pages that name it, and never served.</summary>
    Library,

    /// <summary>Declarations that declare one entry point: started by a page, and never served.</summary>
    Worker,
}
