// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The one write a render may make, shared by its content layer and every embedded layer.</summary>
/// <remarks>
/// A render writes once: two writes in one page are refused today, and a listing that embeds a
/// self-saving widget would quietly make several if each layer owned its own counter.
/// </remarks>
internal sealed class PageWriteAction {

    private int _acted;

    /// <summary>Whether this render has already written.</summary>
    public bool HasActed => Volatile.Read(ref _acted) != 0;

    /// <summary>Records that this render has now written.</summary>
    public void Act() => Interlocked.Exchange(ref _acted, 1);
}
