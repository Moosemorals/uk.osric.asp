// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>A stored revision's template, for the editor and its diff.</summary>
internal sealed record PageRevisionTemplate(
    PagePath Path,
    string Hash,
    DateTimeOffset SavedAt,
    string CreationRuntimeId,
    string ImageStatus,
    string Template);
