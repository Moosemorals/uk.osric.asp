// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The one open transaction a render may have, shared by its content layer and every embedded layer.</summary>
/// <remarks>
/// The store has one transaction and no second model, so a page holds one open at a time and a
/// transaction does not nest. The gate is shared rather than per layer for the reason the old
/// one-action counter was: a listing that embeds a self-saving widget would otherwise give the widget
/// a transaction of its own in the middle of the page's.
/// </remarks>
internal sealed class PageTransactionGate {

    private int _open;

    /// <summary>Whether a transaction is open.</summary>
    public bool IsOpen => Volatile.Read(ref _open) != 0;

    /// <summary>Takes the render's one transaction.</summary>
    /// <returns><see langword="false"/> when one is already open.</returns>
    public bool TryOpen() => Interlocked.CompareExchange(ref _open, 1, 0) == 0;

    /// <summary>Releases the render's one transaction.</summary>
    public void Close() => Interlocked.Exchange(ref _open, 0);
}
