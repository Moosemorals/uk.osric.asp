// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Decides what a revision's kind means for the response.</summary>
/// <remarks>
/// A page with no code is a complete representation: nothing the shell could inject belongs in it, so
/// its response is its source and a URL naming one revision may promise never to change. A page with
/// code is never pinned; it may keep state or vary by visitor. A library and a worker are not
/// representations at all: the first is compiled into the pages that name it, the second is started
/// by a page, and neither has a URL.
/// </remarks>
internal static class PageAssetPolicy {

    /// <summary>Whether a revision is an asset: it has no code.</summary>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsAsset(PageKind? kind) => kind is PageKind.Text;

    /// <summary>Whether a revision is a library: it is compiled into other pages and never served.</summary>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsLibrary(PageKind? kind) => kind is PageKind.Library;

    /// <summary>Whether a revision is a worker: a page starts it and no URL answers for it.</summary>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsWorker(PageKind? kind) => kind is PageKind.Worker;

    /// <summary>Whether a URL may answer with this revision at all.</summary>
    /// <remarks>
    /// An unjudged revision is served, because judging it is what the first request does. This is the
    /// question the endpoint and the renderer ask, so a new kind that is not a representation cannot
    /// be served by forgetting one of them.
    /// </remarks>
    /// <param name="kind">The revision's kind; null means never judged.</param>
    public static bool IsServed(PageKind? kind) => !IsLibrary(kind) && !IsWorker(kind);
}
