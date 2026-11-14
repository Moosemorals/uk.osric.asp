// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Decides what a revision's kind means for the response.</summary>
/// <remarks>
/// A page with no code is a complete representation: nothing the shell could inject belongs in it, so
/// its response is its source and a URL naming one revision may promise never to change. A page with
/// code is never pinned; it may keep state or vary by visitor. A library is not a representation at
/// all: it is compiled into the pages that name it and has no URL.
/// </remarks>
internal static class PageAssetPolicy {

    /// <summary>Whether a revision is an asset: it has no code.</summary>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsAsset(PageKind? kind) => kind is PageKind.Text;

    /// <summary>Whether a revision is a library: it is compiled into other pages and never served.</summary>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsLibrary(PageKind? kind) => kind is PageKind.Library;
}
