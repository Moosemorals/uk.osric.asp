// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>A memory store that records the account sweeps made against it.</summary>
/// <remarks>
/// The account command touches one member of the store, so the double implements that one and refuses
/// the rest: a command that started reading memories would fail here rather than quietly pass.
/// </remarks>
internal sealed class FakeMemoryStore : IMemoryStore {

    /// <summary>The owners whose memories were deleted, in the order they were swept.</summary>
    public List<string> Swept { get; } = [];

    public Task<int> DeleteForOwnerAsync(string owner, CancellationToken cancellationToken = default) {
        Swept.Add(owner);
        return Task.FromResult(0);
    }

    public Task<IReadOnlyList<MemoryCandidate>> RecallAsync(string owner, string queryTerms, int limit, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not recall memory.");

    public Task<IReadOnlyList<MemoryCandidate>> StickyAsync(string owner, long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not recall memory.");

    public Task<IReadOnlyList<MemoryCandidate>> PinnedAsync(string owner, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not recall memory.");

    public Task<MemoryWriteResult> RememberAsync(string owner, MemoryKind kind, string content, MemorySource source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not write memory.");

    public Task<MemoryWriteResult> UpdateAsync(string owner, long id, int expectedVersion, string content, MemorySource source, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not write memory.");

    public Task<bool> ForgetAsync(string owner, long id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not write memory.");

    public Task TouchAsync(string owner, IReadOnlyList<long> ids, long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not recall memory.");

    public Task<int> CountAsync(string owner, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not read memory.");

    public Task<int> ClearProvenanceAsync(long conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The account command does not read memory.");
}
