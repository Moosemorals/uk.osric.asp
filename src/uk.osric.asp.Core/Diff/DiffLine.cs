// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

/// <summary>One line of a diff, with the line numbers it has in each document.</summary>
/// <param name="Kind">Whether the line is context, added or removed.</param>
/// <param name="Text">The line's text, without its terminator.</param>
/// <param name="BeforeLine">The 1-based line number in the earlier document, or null for an added line.</param>
/// <param name="AfterLine">The 1-based line number in the later document, or null for a removed line.</param>
public readonly record struct DiffLine(
    DiffLineKind Kind,
    string Text,
    int? BeforeLine,
    int? AfterLine);
