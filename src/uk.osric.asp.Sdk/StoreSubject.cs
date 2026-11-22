// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Whose rows a declared table holds.</summary>
/// <remarks>
/// A declaration names one of these two and nothing else: a page's table is shared by every visitor
/// and dies with the page, and a visitor's table holds one subject's rows behind an owner column the
/// page never writes. The declaration is where the difference is decided, because it is the one place
/// the host can see it before a row exists.
/// </remarks>
public enum StoreSubject {

    /// <summary>The page itself: every visitor sees the same rows, and deleting the page takes them.</summary>
    Page,

    /// <summary>One subject's rows: the signed-in account, the visit or the browser, by the table's lifetime.</summary>
    Visitor,
}
