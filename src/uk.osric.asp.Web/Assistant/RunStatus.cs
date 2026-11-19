// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>The statuses a run is stored in, named once so the queries cannot drift apart.</summary>
/// <remarks>
/// Every status is here because more than one place has to agree on it: the active-run queries, the
/// recovery sweep and the processor all decide whether a run is still going. Only the partial index's
/// filter spells the active ones out again, because a schema definition is written in the migration
/// rather than read from code.
/// </remarks>
internal static class RunStatus {
    public const string Queued = "queued";
    public const string Running = "running";

    /// <summary>The run is stopped and the owner's answer is what moves it on.</summary>
    public const string AwaitingOwner = "awaiting_owner";

    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";

    /// <summary>Whether a run in this status is still the conversation's active run.</summary>
    /// <param name="status">A stored status.</param>
    public static bool IsActive(string status) =>
        status is Queued or Running or AwaitingOwner;
}
