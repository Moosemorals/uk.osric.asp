// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Several writes that become the store's state together, or not at all.</summary>
/// <remarks>
/// <para>
/// A transaction is the store's own: the writes it stages are applied in one SQLite transaction at
/// <see cref="CommitAsync"/>, after every condition has been proved again against the heads the page
/// staged against. A transaction that is disposed without a commit writes nothing, and a page holds
/// one open at a time.
/// </para>
/// <para>
/// A save compiles when it is staged, so a staged call answers the revision hash and the compiler's
/// diagnostics but not the outcome: whether it is stored is decided at the commit, and the commit's
/// report says so for every operation, one row each. A refusal writes nothing at all — the whole
/// transaction is rolled back and every row says what it saw — so a page that wants the rest must
/// open another transaction with the operations it still wants.
/// </para>
/// <para>
/// A transaction opened while one is already open <strong>nests</strong> inside it: committing the
/// nested one merges its writes into the outer one, so they land when the outer commits and vanish if
/// it is disposed instead. A nested commit stores nothing, so its rows stay
/// <see cref="PageWriteOutcome.Staged"/>; the outer transaction's commit is what decides every
/// operation, including the nested ones, and its report holds a row for each.
/// </para>
/// <para>
/// One operation per key, in one transaction or in one nested inside it: a write on a key that
/// already has one staged answers <see cref="PageWriteOutcome.AlreadyStaged"/> when it is made, and
/// nothing is retried, merged or replaced on the page's behalf.
/// </para>
/// <para>
/// The members keep the arguments of the matching <see cref="IPageWrites"/> members, so a page that
/// can make one write can make several. A write made through <see cref="IPageWrites"/> while this
/// transaction is open joins it and answers Staged, which is how an embedded page's write becomes part
/// of the page's.
/// </para>
/// </remarks>
public interface IPageTransaction : IAsyncDisposable {

    /// <summary>Stages this page's own source, bound to <see cref="IContext.Path"/>.</summary>
    /// <param name="template">The template text. It may be empty; an empty page is a page.</param>
    /// <returns>The staged row: the hash of the revision it will store, and the diagnostics.</returns>
    /// <exception cref="System.ArgumentNullException">The template is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException">This transaction is committed.</exception>
    Task<PageWriteResult> SaveAsync(string template);

    /// <summary>Stages a page, only while its head still has the expected revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedHash">
    /// The head hash the form was rendered from, or <see langword="null"/> when the page had no live
    /// source: a new page, or a tombstone.
    /// </param>
    Task<PageWriteResult> SaveAsync(string key, string template, string? expectedHash);

    /// <summary>Stages a move of a page, its history and its state.</summary>
    /// <param name="from">The key the page has; normalised and checked.</param>
    /// <param name="to">The key it should have; normalised and checked.</param>
    /// <param name="expectedHash">The head hash the caller read.</param>
    Task<PageWriteResult> MoveAsync(string from, string to, string expectedHash);

    /// <summary>Stages a tombstone for a page, keeping its history.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    Task<PageWriteResult> DeleteAsync(string key);

    /// <summary>Stages a retained revision's source as a new revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="hash">The retained revision's hash, as the catalog reported it.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    Task<PageWriteResult> RestoreRevisionAsync(string key, string hash, string? expectedHash);

    /// <summary>Stages recording a seed as installed without storing a revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    Task<PageWriteResult> AdoptSeedAsync(string key);

    /// <summary>Stages a page the release ships with from its bundled source.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    Task<PageWriteResult> InstallSeedAsync(string key, string? expectedHash);

    /// <summary>Writes every staged operation in one transaction, or none of them.</summary>
    /// <returns>One row per operation, in the order they were staged.</returns>
    /// <remarks>
    /// A committed transaction is not committed twice: the second call throws. A refusal does not
    /// throw — it is the report's answer — but a page bug that was detected while staging already has.
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
