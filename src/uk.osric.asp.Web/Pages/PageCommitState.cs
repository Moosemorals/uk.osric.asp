// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Whether one layer of a render has committed its response.</summary>
/// <remarks>
/// Shared by the context that flushes and the capabilities that would need to set a header after it.
/// The flag is read by the page's own task and by the abandoning thread, so it is volatile rather
/// than merely lock-protected.
/// </remarks>
internal sealed class PageCommitState {

    private volatile bool _committed;

    /// <summary>Whether the first flush has happened.</summary>
    public bool Committed {
        get => _committed;
        set => _committed = value;
    }
}
