// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>What one run's recall chose, and what it cost.</summary>
/// <param name="Memories">The chosen memories, in the order the block lists them.</param>
/// <param name="Characters">What the block costs: each memory's content and its tag allowance.</param>
/// <param name="Truncated">Whether a candidate that fitted the count did not fit the characters.</param>
public sealed record MemorySelection(
    IReadOnlyList<MemoryCandidate> Memories,
    int Characters,
    bool Truncated) {

    /// <summary>Whether the run was given no memory at all.</summary>
    public bool IsEmpty => Memories.Count == 0;
}
