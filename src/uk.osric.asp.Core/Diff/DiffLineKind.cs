// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

/// <summary>What one line of a diff represents.</summary>
public enum DiffLineKind {

    /// <summary>The line is in both documents.</summary>
    Context,

    /// <summary>The line is only in the later document.</summary>
    Added,

    /// <summary>The line is only in the earlier document.</summary>
    Removed,
}
