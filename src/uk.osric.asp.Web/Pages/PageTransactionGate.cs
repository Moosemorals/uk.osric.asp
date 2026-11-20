// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The transaction a render's writes join, shared by its content layer and every embedded layer.</summary>
/// <remarks>
/// A write joins the innermost transaction that is open, so a nested transaction owns the writes made
/// while it is open — which is what makes its rollback take only those — and committing it merges them
/// into the one it nests inside. The gate is shared rather than per layer because an embedded page is
/// another layer of the same render: its writes belong to the page's unit, not to a second one writing
/// alongside it.
/// </remarks>
internal sealed class PageTransactionGate {

    private PageWriteTransaction? _current;

    /// <summary>The innermost transaction that is open, or <see langword="null"/> when none is.</summary>
    public PageWriteTransaction? Current => Volatile.Read(ref _current);

    /// <summary>Makes a transaction the one later writes join.</summary>
    /// <param name="transaction">The transaction that has just opened.</param>
    public void Open(PageWriteTransaction transaction) {
        ArgumentNullException.ThrowIfNull(transaction);

        Volatile.Write(ref _current, transaction);
    }

    /// <summary>Puts the transaction a closing one nests inside back in charge.</summary>
    /// <param name="transaction">The transaction that is closing.</param>
    public void Close(PageWriteTransaction transaction) {
        ArgumentNullException.ThrowIfNull(transaction);

        // Only the innermost one closing matters: an outer transaction that is disposed while a nested
        // one is open leaves the nested one exactly as it was.
        if (ReferenceEquals(Current, transaction)) {
            Volatile.Write(ref _current, transaction.Parent);
        }
    }
}
