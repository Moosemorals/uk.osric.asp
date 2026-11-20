// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What checking a source answered.</summary>
/// <param name="Kind">
/// What the source turned out to be, or <see langword="null"/> when the check could not say — a
/// script has no page kind, and a check that ran out of time discovered nothing.
/// </param>
/// <param name="Valid">Whether the source would be served; a warning leaves it valid.</param>
/// <param name="Diagnostics">What the checker found, in the order an author reads it.</param>
/// <remarks>
/// The answer is the fresh verdict for the text that was passed, not what the last save stored:
/// nothing is stored beside it, so the same text answers the same way whether or not a page holds it.
/// </remarks>
public sealed record PageCheckResult(
    PageKind? Kind,
    bool Valid,
    IReadOnlyList<PageDiagnostic> Diagnostics);
