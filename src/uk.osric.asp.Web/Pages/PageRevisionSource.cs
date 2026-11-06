// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>A stored revision with its source, for the editor and its diff.</summary>
internal sealed record PageRevisionSource(
    PagePath Path,
    string Hash,
    DateTimeOffset SavedAt,
    string CreationRuntimeId,
    string CompilationStatus,
    string Content);
