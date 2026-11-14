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
/// against, so it is never served and never given a render.
/// </para>
/// </remarks>
public enum PageKind {

    /// <summary>No C# at all, so the stored source is the response byte for byte.</summary>
    Text,

    /// <summary>C# that renders: it is compiled to a handler and wrapped in the site layout.</summary>
    Page,

    /// <summary>Only declarations: compiled into the pages that name it, and never served.</summary>
    Library,
}
