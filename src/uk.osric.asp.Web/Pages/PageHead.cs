// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The storage row that points a page key at its current revision.</summary>
internal sealed class PageHead {
    public required string Path { get; init; }

    public required string RevisionId { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>The content hash of the seed this page's content is anchored to, or null when unknown.</summary>
    public string? SeedHash { get; set; }

    /// <summary>The content hash of the head's stored source.</summary>
    public string? ContentHash { get; set; }
}
