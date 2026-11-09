// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>The assistant's owner-scoped memory, as the run and the tools use it.</summary>
/// <remarks>
/// Every method takes the owner and every statement filters on it, so a wrong owner and a missing
/// memory answer the same way and a probe cannot tell them apart. The owner never comes from the model
/// (DEC-184).
/// </remarks>
internal interface IMemoryStore {

    /// <summary>The owner's memories that match an FTS5 expression, best match first.</summary>
    /// <param name="owner">The account whose memories are searched.</param>
    /// <param name="queryTerms">The expression <see cref="MemoryQuery.Terms"/> built.</param>
    /// <param name="limit">The most candidates to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<MemoryCandidate>> RecallAsync(
        string owner,
        string queryTerms,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>The owner's memories this conversation has already used, most recently used first.</summary>
    /// <param name="owner">The account whose memories are read.</param>
    /// <param name="conversationId">The conversation whose recall is sticky.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<MemoryCandidate>> StickyAsync(
        string owner,
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>The owner's pinned memories, newest change first.</summary>
    /// <param name="owner">The account whose memories are read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<MemoryCandidate>> PinnedAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>Every memory the owner holds, newest change first, for the owner's own page.</summary>
    /// <param name="owner">The account whose memories are read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<MemoryCandidate>> ListAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>One memory the owner holds, or null when there is no such memory for them.</summary>
    /// <param name="owner">The account whose memory is read.</param>
    /// <param name="id">The memory's id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<MemoryCandidate?> GetAsync(string owner, long id, CancellationToken cancellationToken = default);

    /// <summary>The owner's edit of a memory: its content, its kind and its pin.</summary>
    /// <remarks>
    /// This is the only write that can set <see cref="MemoryCandidate.Pinned"/>. The assistant's
    /// <see cref="UpdateAsync"/> has no such argument, so a model cannot pin a memory by any path
    /// (DEC-201).
    /// </remarks>
    /// <param name="owner">The account the memory belongs to.</param>
    /// <param name="id">The memory to change.</param>
    /// <param name="expectedVersion">The version the page was rendered from; a stale one is refused.</param>
    /// <param name="kind">The kind the owner chose.</param>
    /// <param name="content">The note as the owner wrote it.</param>
    /// <param name="pinned">Whether the owner pinned it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<MemoryWriteResult> EditAsync(
        string owner,
        long id,
        int expectedVersion,
        MemoryKind kind,
        string content,
        bool pinned,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a memory, or returns the row that already says the same thing.</summary>
    /// <param name="owner">The account the memory belongs to.</param>
    /// <param name="kind">What kind of note it is.</param>
    /// <param name="content">The note, as the model wrote it.</param>
    /// <param name="source">The conversation and run that wrote it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<MemoryWriteResult> RememberAsync(
        string owner,
        MemoryKind kind,
        string content,
        MemorySource source,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces a memory's content, keeping the version it replaced as history.</summary>
    /// <param name="owner">The account the memory belongs to.</param>
    /// <param name="id">The memory to change.</param>
    /// <param name="expectedVersion">The version the caller read; a stale one is refused.</param>
    /// <param name="content">The new note.</param>
    /// <param name="source">The conversation and run that made the edit.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<MemoryWriteResult> UpdateAsync(
        string owner,
        long id,
        int expectedVersion,
        string content,
        MemorySource source,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a memory and its history.</summary>
    /// <param name="owner">The account the memory belongs to.</param>
    /// <param name="id">The memory to forget.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Whether a memory was there to forget.</returns>
    Task<bool> ForgetAsync(string owner, long id, CancellationToken cancellationToken = default);

    /// <summary>Records that a run used these memories.</summary>
    /// <param name="owner">The account the memories belong to.</param>
    /// <param name="ids">The memories the run was given.</param>
    /// <param name="conversationId">The conversation that used them.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    Task TouchAsync(
        string owner,
        IReadOnlyList<long> ids,
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>How many memories the owner holds, for the cap the page shows.</summary>
    /// <param name="owner">The account to count.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<int> CountAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>Drops the provenance of the memories a deleted conversation wrote, keeping them.</summary>
    /// <param name="conversationId">The conversation being deleted.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>How many memories lost their provenance.</returns>
    Task<int> ClearProvenanceAsync(long conversationId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every memory an account holds, which is what removing the account does.</summary>
    /// <param name="owner">The account being removed.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many memories were removed.</returns>
    Task<int> DeleteForOwnerAsync(string owner, CancellationToken cancellationToken = default);
}
