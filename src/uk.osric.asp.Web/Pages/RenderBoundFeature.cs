// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The state every capability the render can stop shares: its deadline and its abandonment.</summary>
/// <remarks>
/// A page's task and the abandoning thread can be different threads, so the flag is read with
/// <see cref="Volatile"/> rather than through a lock the page never takes.
/// </remarks>
internal abstract class RenderBoundFeature : IRenderBoundFeature {

    private int _abandoned;
    private CancellationToken _deadline;

    /// <summary>Whether the render was given up on.</summary>
    protected bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

    /// <summary>The render's deadline, which ends a call that outlives it.</summary>
    protected CancellationToken Deadline => _deadline;

    /// <inheritdoc/>
    public void AttachDeadline(CancellationToken deadline) => _deadline = deadline;

    /// <inheritdoc/>
    public void Abandon() => Volatile.Write(ref _abandoned, 1);
}
