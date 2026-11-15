// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a job is doing, or how it ended.</summary>
public enum PageJobState {

    /// <summary>Accepted, and waiting for a runner.</summary>
    Queued,

    /// <summary>The worker is running.</summary>
    Running,

    /// <summary>The worker returned.</summary>
    Completed,

    /// <summary>The worker threw, or its image could not be loaded.</summary>
    Failed,

    /// <summary>A page asked it to stop, or the host did.</summary>
    Cancelled,
}
