// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One row of the page list: the key, whether the page is deleted, and its two hashes.</summary>
/// <param name="Path">The canonical key.</param>
/// <param name="IsDeleted">Whether the row is a tombstone.</param>
/// <param name="SeedHash">The seed the page's content is anchored to, or null when unknown.</param>
/// <param name="ContentHash">The content hash of the head's stored source.</param>
internal sealed record PageHeadSummary(PagePath Path, bool IsDeleted, string? SeedHash = null, string? ContentHash = null);
