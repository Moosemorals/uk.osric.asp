// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>A memory store holding a fixed few notes, for the shell views and their accessibility sweep.</summary>
/// <remarks>
/// The view only reads, so the double implements the two reads the page makes and refuses the rest: a
/// view that started writing memory would fail here rather than quietly pass.
/// </remarks>
internal sealed class ShellMemoryStore : IMemoryStore {

    private static readonly DateTimeOffset s_written = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private static readonly MemoryCandidate[] s_memories = [
        new(1, MemoryKind.Preference, "The owner writes British English.", 2, s_written, s_written, 4, Pinned: true, SourceConversationId: 1),
        new(2, MemoryKind.Fact, "The site runs behind Caddy on a Hetzner CAX21.", 1, s_written, null, 0, Pinned: false, SourceConversationId: 1),
        new(3, MemoryKind.Reference, "The deploy script is in the repository root.", 1, s_written, null, 0, Pinned: false),
        new(4, MemoryKind.Feedback, "Keep answers short.", 1, s_written, null, 1, Pinned: false, SourceConversationId: 1),
    ];

    public Task<IReadOnlyList<MemoryCandidate>> ListAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryCandidate>>(s_memories);

    public Task<MemoryCandidate?> GetAsync(string owner, long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(s_memories.FirstOrDefault(memory => memory.Id == id));

    public Task<int> CountAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(s_memories.Length);

    public Task<IReadOnlyList<MemoryCandidate>> RecallAsync(string owner, string queryTerms, int limit, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not recall memory.");

    public Task<IReadOnlyList<MemoryCandidate>> StickyAsync(string owner, long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not recall memory.");

    public Task<IReadOnlyList<MemoryCandidate>> PinnedAsync(string owner, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not recall memory.");

    public Task<MemoryWriteResult> RememberAsync(string owner, MemoryKind kind, string content, MemorySource source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");

    public Task<MemoryWriteResult> UpdateAsync(string owner, long id, int expectedVersion, string content, MemorySource source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");

    public Task<MemoryWriteResult> EditAsync(string owner, long id, int expectedVersion, MemoryKind kind, string content, bool pinned, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");

    public Task<bool> ForgetAsync(string owner, long id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");

    public Task TouchAsync(string owner, IReadOnlyList<long> ids, long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not recall memory.");

    public Task<int> ClearProvenanceAsync(long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");

    public Task<int> DeleteForOwnerAsync(string owner, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A shell view does not write memory.");
}
