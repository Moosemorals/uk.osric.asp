// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The entry point of a stored worker.</summary>
/// <remarks>
/// A worker is a stored source that declares exactly one public type implementing this interface, and
/// that type is the whole program: a page starts one, and the host creates an instance and calls
/// <see cref="RunAsync"/> once for the job. A worker renders nothing — it has no response, no layout
/// and no session — so everything it has to say it says through its context.
/// </remarks>
public interface IWorker {

    /// <summary>Runs the worker for one job.</summary>
    /// <param name="context">What this job is and what the worker may use while it runs.</param>
    /// <returns>A task that completes when the job is finished.</returns>
    public Task RunAsync(IWorkerContext context);
}
