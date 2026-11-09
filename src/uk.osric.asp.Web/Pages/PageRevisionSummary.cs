// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;
/// <summary>One row of a page's revision history.</summary>
internal sealed record PageRevisionSummary(
    string Hash,
    DateTimeOffset SavedAt,
    string CreationRuntimeId,
    PageImageState ImageState,
    bool IsCurrent);
