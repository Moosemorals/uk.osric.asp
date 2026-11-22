// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The rule that decides when a stored entry dies.</summary>
/// <remarks>
/// A lifetime is a property of the entry, not of the store. <see cref="Default"/> is the subject's
/// own rule and is what a write without a lifetime means; the rest name a rule explicitly.
/// </remarks>
public enum StoreLifetimeKind {

    /// <summary>The subject's own default: <see cref="Page"/> on a Page subject, <see cref="Person"/> on a Visitor.</summary>
    Default,

    /// <summary>Dies when the page goes: a delete, a scrub, or a move that leaves nothing.</summary>
    Page,

    /// <summary>Dies when the account goes; anonymously, when the session does, unless adoption moves it.</summary>
    Person,

    /// <summary>Dies when the session rotates or the identity row is swept.</summary>
    Session,

    /// <summary>Dies when the browser's cookie expires or is cleared; survives signing in and out.</summary>
    Browser,

    /// <summary>Dies at the next successful take of the key, atomically.</summary>
    NextRead,
}
