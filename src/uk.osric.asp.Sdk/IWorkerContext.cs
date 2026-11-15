// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a running worker is told about the job it is running.</summary>
/// <remarks>
/// <para>
/// It is deliberately not an <see cref="IContext"/>: a worker has no response to shape and no visitor
/// waiting on it, so it is offered no way to write, flush, redirect or set a status. What it can do —
/// receive messages, publish progress, read and write the starting page's state — arrives with the
/// runner that supplies one of these.
/// </para>
/// <para>
/// A job is started by a signed-in visitor, so <see cref="User"/> is always the owner: a worker acts
/// as that account for as long as the job runs.
/// </para>
/// </remarks>
public interface IWorkerContext {

    /// <summary>The canonical key of the worker source that is running.</summary>
    public string Worker { get; }

    /// <summary>The visitor the job runs for.</summary>
    public PageUser User { get; }
}
