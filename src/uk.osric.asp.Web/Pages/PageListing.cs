// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One row of the page list: the key, whether it is deleted, and the state of its image.</summary>
/// <param name="Path">The canonical key the page is stored under.</param>
/// <param name="IsDeleted">Whether the page is a tombstone, retaining its history.</param>
/// <param name="State">What the page's current revision compiled to, judged against the running build.</param>
internal sealed record PageListing(PagePath Path, bool IsDeleted, PageImageState State);
