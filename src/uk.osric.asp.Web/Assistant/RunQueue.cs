// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Web.Work;

/// <summary>A run waiting for a worker.</summary>
internal sealed record QueuedRun(long ConversationId, string RunId, string Owner);

/// <summary>The bounded queue of runs waiting for a worker.</summary>
/// <remarks>
/// A run the queue cannot hold is refused rather than queued, and the worker runs one at a time
/// because there is one compile slot behind it.
/// </remarks>
internal sealed class RunQueue(AssistantOptions options) : BoundedWorkQueue<QueuedRun>(Capacity(options)) {

    private static int Capacity(AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return options.MaxQueuedRuns;
    }
}
