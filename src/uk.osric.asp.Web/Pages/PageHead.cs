// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The storage row that points a page key at its current revision.</summary>
internal sealed class PageHead {
    public required string Path { get; init; }

    public required string RevisionId { get; set; }

    public bool IsDeleted { get; set; }
}
