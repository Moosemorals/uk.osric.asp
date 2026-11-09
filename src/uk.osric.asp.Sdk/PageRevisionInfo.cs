// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One row of a page's history.</summary>
/// <param name="Hash">The revision's hash, which <see cref="IPageCatalog.ReadRevisionAsync"/> takes.</param>
/// <param name="SavedAt">When the revision was saved.</param>
/// <param name="IsCurrent">
/// Whether the page's live head points at this revision. Always <see langword="false"/> for a
/// tombstoned page, which has no live head.
/// </param>
public readonly record struct PageRevisionInfo(
    string Hash,
    DateTimeOffset SavedAt,
    bool IsCurrent);
