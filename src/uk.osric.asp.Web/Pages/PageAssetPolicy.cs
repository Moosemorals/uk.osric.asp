// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Decides whether a revision is an asset.</summary>
/// <remarks>
/// A page with no code is a complete representation: nothing the shell could inject belongs in it, so
/// its response is its source and a URL naming one revision may promise never to change. A page with
/// code is never pinned; it may keep state or vary by visitor.
/// </remarks>
internal static class PageAssetPolicy {

    /// <summary>Whether a revision is an asset: it has no code.</summary>
    /// <param name="templateHasCode">The revision's verdict; null means never judged.</param>
    public static bool IsAsset(bool? templateHasCode) => templateHasCode is false;
}
