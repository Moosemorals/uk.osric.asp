// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class MemoryListingTests {

    private static readonly DateTimeOffset s_base = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    [Test]
    public void KindsAreListedInTheirOwnOrder() {
        MemoryCandidate[] memories = [
            Memory(1, MemoryKind.Reference, "where the deploy script lives"),
            Memory(2, MemoryKind.Preference, "British English"),
            Memory(3, MemoryKind.Feedback, "short answers please"),
            Memory(4, MemoryKind.Fact, "runs on Podman"),
        ];

        IReadOnlyList<MemoryGroup> groups = MemoryListing.Group(memories);

        Assert.That(groups.Select(group => group.Kind), Is.EqualTo([
            MemoryKind.Preference, MemoryKind.Fact, MemoryKind.Feedback, MemoryKind.Reference,
        ]));
        Assert.That(groups.Select(group => group.Memories.Single().Id), Is.EqualTo([2, 4, 3, 1]));
    }

    [Test]
    public void AKindWithNothingInItIsLeftOut() {
        MemoryCandidate[] memories = [Memory(1, MemoryKind.Fact, "one"), Memory(2, MemoryKind.Fact, "two")];

        IReadOnlyList<MemoryGroup> groups = MemoryListing.Group(memories);

        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Kind, Is.EqualTo(MemoryKind.Fact));
    }

    [Test]
    public void PinnedMemoriesComeFirstInsideTheirKind() {
        MemoryCandidate[] memories = [
            Memory(1, MemoryKind.Preference, "newest but unpinned", written: s_base.AddDays(2)),
            Memory(2, MemoryKind.Preference, "oldest but pinned", written: s_base, pinned: true),
        ];

        IReadOnlyList<MemoryGroup> groups = MemoryListing.Group(memories);

        Assert.That(groups[0].Memories.Select(memory => memory.Id), Is.EqualTo([2, 1]));
    }

    [Test]
    public void TheNewestChangeComesFirstAfterThePinnedOnes() {
        MemoryCandidate[] memories = [
            Memory(1, MemoryKind.Fact, "old", written: s_base),
            Memory(2, MemoryKind.Fact, "new", written: s_base.AddDays(1)),
            Memory(3, MemoryKind.Fact, "middle", written: s_base.AddHours(1)),
        ];

        Assert.That(MemoryListing.Group(memories)[0].Memories.Select(memory => memory.Id), Is.EqualTo([2, 3, 1]));
    }

    [Test]
    public void TheIdBreaksATieSoTheOrderIsStable() {
        MemoryCandidate[] memories = [
            Memory(1, MemoryKind.Fact, "first", written: s_base),
            Memory(2, MemoryKind.Fact, "second", written: s_base),
        ];

        Assert.That(MemoryListing.Group(memories)[0].Memories.Select(memory => memory.Id), Is.EqualTo([2, 1]));
        Assert.That(MemoryListing.Group([.. memories.Reverse()])[0].Memories.Select(memory => memory.Id),
            Is.EqualTo([2, 1]), "the order does not depend on the input order");
    }

    [Test]
    public void NothingRememberedIsNoGroupsRatherThanFourEmptyHeadings() {
        Assert.That(MemoryListing.Group([]), Is.Empty);
    }

    private static MemoryCandidate Memory(
        long id,
        MemoryKind kind,
        string content,
        DateTimeOffset? written = null,
        bool pinned = false) =>
        new(id, kind, content, 1, written ?? s_base, LastUsed: null, RecallCount: 0, pinned);
}
