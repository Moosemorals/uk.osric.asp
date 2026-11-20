// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Several writes that become the store's state together, or not at all.</summary>
/// <remarks>
/// <para>
/// It is the frame, not a second copy of the write members: a write made through
/// <see cref="IPageWrites"/> while this is open joins it, answers
/// <see cref="PageWriteOutcome.Staged"/>, and lands when this commits. So a page opens one, uses the
/// members it already knows, and decides once at the end.
/// </para>
/// <para>
/// The commit applies the writes in the order they were made, each seeing what the ones before it
/// wrote: create a page, move it and delete it in one transaction and all three succeed, because the
/// move reads the page the create made and the delete tombstones the head the move left. A refusal
/// writes nothing — the whole transaction is rolled back — and the report says which write refused,
/// which were applied and undone, and which were never reached.
/// </para>
/// <para>
/// It is the store's own transaction: taken when the commit starts, holding the one writer from its
/// first statement, and statements only — nothing is compiled inside it. A transaction opened while
/// one is open nests inside it: the nested commit merges its writes into the outer one, so they land
/// when the outer commits and vanish if it is rolled back instead, and the nested transaction's own
/// rows stay <see cref="PageWriteOutcome.Staged"/>.
/// </para>
/// <para>
/// Letting it go out of scope without committing writes nothing, which is the same as
/// <see cref="RollbackAsync"/>. Nothing may be written into it once it has been committed or rolled
/// back (`pages-writes-are-transactions`).
/// </para>
/// <para>
/// A worker's commit also proves the credential the job borrowed is still live. If it is not, the
/// commit stores nothing and answers <see cref="PageWriteOutcome.Revoked"/> for every operation.
/// </para>
/// </remarks>
public interface IPageTransaction : IAsyncDisposable {

    /// <summary>Writes every staged operation in one transaction, or none of them.</summary>
    /// <returns>One row per operation, in the order they were staged.</returns>
    /// <remarks>
    /// A refusal does not throw — it is the report's answer — but a page bug detected while staging
    /// already has.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">This transaction was already committed or rolled back.</exception>
    Task<IReadOnlyList<PageWriteResult>> CommitAsync();

    /// <summary>Discards every staged write, writing nothing.</summary>
    /// <returns>One row per operation, each saying it was never attempted.</returns>
    /// <remarks>
    /// It is the explicit abort, and it does the same thing as letting the transaction go out of
    /// scope: a transaction that is never committed writes nothing, so a page only needs this when it
    /// wants to abandon the work and keep going in the same render. A nested transaction rolls back
    /// only its own writes; the transaction it is inside is unaffected.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">This transaction was already committed or rolled back.</exception>
    Task<IReadOnlyList<PageWriteResult>> RollbackAsync();
}
