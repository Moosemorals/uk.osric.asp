// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A subject's tables, as a page reaches them: one statement at a time.</summary>
/// <remarks>
/// <para>
/// The statements live here and a transaction is only the frame, exactly as <c>IPageWrites</c> carries
/// the writes and <c>IPageTransaction</c> carries one commit: a second spelling of
/// <see cref="QueryAsync"/> on the handle would be the copy that rule exists to prevent.
/// </para>
/// <para>
/// A statement is one statement of text and its parameters. Not because raw SQL is a taste, but because
/// the page API leaves nothing else standing: EF Core and <c>Microsoft.Data.Sqlite</c> are denied,
/// <c>System.Data</c> is absent and <c>System.Linq.Expressions</c> is denied, so the text is what a page
/// can write and what the sandbox can police.
/// </para>
/// <para>
/// Everything the page itself got wrong throws: a statement the authorizer denies, a table the
/// declaration does not have, a write to a row the reader does not own, a second statement, a result
/// past its cap, a <c>HEAD</c>, an abandoned render or a disposed handle. The fence is one statement
/// deep rather than a claim of safety.
/// </para>
/// </remarks>
public interface IRecordStore {

    /// <summary>Runs one statement and answers its rows.</summary>
    /// <param name="sql">One <c>SELECT</c> or <c>WITH</c> statement.</param>
    /// <param name="parameters">The statement's parameters, or <see langword="null"/> for none.</param>
    /// <returns>
    /// The rows, in the order the statement produced them, up to <c>MaxResultRows</c>; a row past the
    /// cap throws naming the setting.
    /// </returns>
    /// <exception cref="ArgumentException">The text is not exactly one statement, or a parameter is unused.</exception>
    /// <exception cref="InvalidOperationException">The statement was refused, or this render may not read.</exception>
    public Task<IReadOnlyList<PageRow>> QueryAsync(string sql, PageParameters? parameters = null);

    /// <summary>Runs one statement that writes, and answers what became of it.</summary>
    /// <param name="sql">One <c>INSERT</c>, <c>UPDATE</c> or <c>DELETE</c> statement.</param>
    /// <param name="parameters">The statement's parameters, or <see langword="null"/> for none.</param>
    /// <returns>The rows changed, and whether the write is durable or staged.</returns>
    /// <exception cref="ArgumentException">The text is not exactly one statement.</exception>
    /// <exception cref="InvalidOperationException">The statement was refused, or this render may not write.</exception>
    public Task<PageRecordResult> ExecuteAsync(string sql, PageParameters? parameters = null);

    /// <summary>Finds the subject's rows of one declared table that match a term.</summary>
    /// <param name="table">The table's declared name.</param>
    /// <param name="term">The FTS5 match expression.</param>
    /// <param name="limit">The most rows to answer, or <see langword="null"/> for <c>MaxResultRows</c>.</param>
    /// <returns>The subject's rows, best match first, bounded by the result cap.</returns>
    /// <remarks>
    /// A call rather than SQL because a view cannot carry <c>MATCH</c> and the index holds every
    /// subject's rows: an index a page could query directly would leak the existence of another
    /// subject's rows.
    /// </remarks>
    /// <exception cref="ArgumentException">The table is not declared, or declares no search column.</exception>
    public Task<IReadOnlyList<PageRow>> SearchAsync(string table, string term, int? limit = null);

    /// <summary>Opens a transaction over this handle's connection.</summary>
    /// <returns>The frame: statements run inside it until it commits or rolls back.</returns>
    /// <exception cref="InvalidOperationException">
    /// A transaction is already open on this handle, or this render may not write.
    /// </exception>
    public IPageRecordTransaction BeginTransaction();
}

/// <summary>One open transaction: the frame a page's statements run inside.</summary>
/// <remarks>
/// It carries no statement methods, because the store already has them and an open transaction only
/// changes what they mean: a write answers <see cref="PageRecordOutcome.Staged"/> and a later query reads
/// what it staged. One handle holds one transaction at a time, and disposing the frame without a commit
/// discards everything since it began.
/// </remarks>
public interface IPageRecordTransaction : IAsyncDisposable {

    /// <summary>Commits everything written since the transaction began, and ends it.</summary>
    /// <returns>A task that completes when the commit is durable.</returns>
    /// <exception cref="InvalidOperationException">The transaction has already ended.</exception>
    public Task CommitAsync();

    /// <summary>Discards everything written since the transaction began, and ends it.</summary>
    /// <returns>A task that completes when the rollback is done.</returns>
    /// <exception cref="InvalidOperationException">The transaction has already ended.</exception>
    public Task RollbackAsync();
}
