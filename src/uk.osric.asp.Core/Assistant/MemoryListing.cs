// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>One kind's memories, as the owner's page draws them.</summary>
/// <param name="Kind">The kind the heading names.</param>
/// <param name="Memories">Its memories, pinned first and newest change first after that.</param>
public sealed record MemoryGroup(MemoryKind Kind, IReadOnlyList<MemoryCandidate> Memories);

/// <summary>The order the owner's page lists memories in.</summary>
/// <remarks>
/// The rule is pure so the page has nothing to decide: the four kinds in their own order, a kind with
/// nothing in it left out, and inside a kind the pinned memories first — the owner asked for those by
/// pinning them — then the newest change. The id breaks a tie, so two memories written in the same
/// instant keep one order between two renders.
/// </remarks>
public static class MemoryListing {

    /// <summary>Groups a listing by kind and orders each group.</summary>
    /// <param name="memories">The owner's memories, in any order.</param>
    /// <returns>One group per kind that has memories, in kind order.</returns>
    public static IReadOnlyList<MemoryGroup> Group(IReadOnlyList<MemoryCandidate> memories) {
        ArgumentNullException.ThrowIfNull(memories);

        List<MemoryGroup> groups = [];
        foreach (MemoryKind kind in MemoryKinds.All) {
            MemoryCandidate[] held = [.. memories
                .Where(memory => memory.Kind == kind)
                .OrderByDescending(memory => memory.Pinned)
                .ThenByDescending(memory => memory.Written)
                .ThenByDescending(memory => memory.Id)];
            if (held.Length > 0) {
                groups.Add(new MemoryGroup(kind, held));
            }
        }

        return groups;
    }
}
