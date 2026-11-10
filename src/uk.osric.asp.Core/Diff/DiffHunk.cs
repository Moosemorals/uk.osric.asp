// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

/// <summary>A contiguous group of changed lines and their context.</summary>
/// <param name="BeforeStart">The first before line number the hunk covers, or the line it follows when it covers none.</param>
/// <param name="BeforeCount">How many lines the hunk covers in the earlier document.</param>
/// <param name="AfterStart">The first after line number the hunk covers, or the line it follows when it covers none.</param>
/// <param name="AfterCount">How many lines the hunk covers in the later document.</param>
/// <param name="Lines">The hunk's context, removed and added lines, in document order.</param>
public readonly record struct DiffHunk(
    int BeforeStart,
    int BeforeCount,
    int AfterStart,
    int AfterCount,
    IReadOnlyList<DiffLine> Lines);
