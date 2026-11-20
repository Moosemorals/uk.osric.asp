// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Which write a commit's report row is about.</summary>
public enum PageWriteOperation {

    /// <summary>This page's own source, the bound save.</summary>
    Save,

    /// <summary>A named key's source.</summary>
    Edit,

    /// <summary>A key moved with its history and state.</summary>
    Move,

    /// <summary>A key tombstoned, keeping its history.</summary>
    Delete,

    /// <summary>A retained revision stored as a new one.</summary>
    Restore,

    /// <summary>A seed the release ships stored as a new revision.</summary>
    Install,

    /// <summary>A seed recorded as installed without storing a revision.</summary>
    Adopt,
}
