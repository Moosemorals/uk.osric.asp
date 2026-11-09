// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>One memory as recall and the tools see it: the head row, without its history.</summary>
/// <param name="Id">The memory's id, which is what a tool call names and the block labels.</param>
/// <param name="Kind">What kind of note it is.</param>
/// <param name="Content">The note's text, already normalised.</param>
/// <param name="Version">The version a later update has to carry to be accepted.</param>
/// <param name="Written">When this version was written.</param>
/// <param name="LastUsed">When it was last recalled, or null when it never was.</param>
/// <param name="RecallCount">How many runs have recalled it.</param>
/// <param name="Pinned">Whether the owner pinned it, which only the owner may change.</param>
public sealed record MemoryCandidate(
    long Id,
    MemoryKind Kind,
    string Content,
    int Version,
    DateTimeOffset Written,
    DateTimeOffset? LastUsed,
    int RecallCount,
    bool Pinned);
