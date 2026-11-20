// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The outermost transaction a render has open, shared by its content layer and every embedded layer.</summary>
/// <remarks>
/// A render has one outer transaction at a time, and a layer may open a transaction inside it: the
/// nested one merges its writes into this one, so they land with it or not at all. The gate is shared
/// rather than per layer because an embedded page is another layer of the same render, and its
/// transaction must be part of the page's rather than a second transaction writing underneath it.
/// </remarks>
internal sealed class PageTransactionGate {

    private PageTransactionScope? _root;

    /// <summary>The outermost transaction, or <see langword="null"/> when the render has none open.</summary>
    public PageTransactionScope? Root => Volatile.Read(ref _root);

    /// <summary>Records the transaction a later one nests inside.</summary>
    /// <param name="root">The outermost transaction.</param>
    public void Open(PageTransactionScope root) {
        ArgumentNullException.ThrowIfNull(root);

        Volatile.Write(ref _root, root);
    }

    /// <summary>Forgets the outermost transaction once it is committed or disposed.</summary>
    /// <param name="root">The transaction that is closing.</param>
    public void Close(PageTransactionScope root) {
        ArgumentNullException.ThrowIfNull(root);

        // Only the transaction that opened the render's transaction closes it: a nested one that is
        // disposed must leave the outer one exactly as it was.
        if (ReferenceEquals(Root, root)) {
            Volatile.Write(ref _root, null);
        }
    }
}
