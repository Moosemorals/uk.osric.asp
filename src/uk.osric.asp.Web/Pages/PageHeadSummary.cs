// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One row of the page list: the key, and whether the page is deleted.</summary>
internal sealed record PageHeadSummary(PagePath Path, bool IsDeleted);
