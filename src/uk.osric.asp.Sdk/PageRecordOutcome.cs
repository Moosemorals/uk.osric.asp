// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What became of a write.</summary>
/// <remarks>
/// <see cref="Applied"/> and <see cref="Staged"/> answer whether the write is durable yet, which is the
/// whole difference an open transaction makes. <see cref="Busy"/> and <see cref="Full"/> are values
/// rather than exceptions because they depend on data the page did not choose: another writer held the
/// store, or the subject was at its cap. Everything the page got wrong throws instead.
/// </remarks>
public enum PageRecordOutcome {

    /// <summary>Written and durable: no transaction was open, so the statement was a transaction of one.</summary>
    Applied,

    /// <summary>Written inside the page's transaction and durable only at its commit.</summary>
    Staged,

    /// <summary>Another writer held the store, so nothing was written.</summary>
    Busy,

    /// <summary>The subject is at its row or byte cap, so nothing was written.</summary>
    Full,
}
