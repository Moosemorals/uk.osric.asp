// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a listing says about a page's relation to the seed this release ships.</summary>
/// <param name="Verdict">
/// The verdict in the host's words — "in sync", "release newer", "edited", "conflict", "store newer",
/// "unknown", "retired", "new" or "deleted".
/// </param>
/// <param name="Sentence">One sentence the page can show beside the row, in the host's words.</param>
/// <param name="CanApply">Whether the host will let this row be replaced with the shipped seed.</param>
/// <param name="CanAdopt">
/// Whether the host will record the shipped seed as installed without changing the content.
/// </param>
/// <remarks>
/// The marks are the host's rules reported rather than derived, exactly as <see cref="PageEntry.State"/>
/// is: a page renders the sentence and offers the actions the flags permit, and it never compares a
/// hash or decides whether a write is safe. The verdict is a string because this assembly references
/// nothing, the core's comparison included.
/// </remarks>
public sealed record PageSeedSummary(string Verdict, string Sentence, bool CanApply, bool CanAdopt);
