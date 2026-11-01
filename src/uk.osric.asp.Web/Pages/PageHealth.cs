// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Counts the page renders that outran their budget and were abandoned.</summary>
/// <remarks>
/// A lost thread cannot be reclaimed, but it can be noticed: past <see cref="Limit"/> the site stops
/// reporting healthy and the supervisor restarts the container. That is the only kill this design
/// has, so the count is what makes it happen. A page may still finish after it was given up on, which
/// is why the count can go down again.
/// </remarks>
public sealed class PageHealth(int abandonedLimit) {

    private int _abandoned;

    /// <summary>Abandoned renders allowed before the site reports unhealthy.</summary>
    public int Limit { get; } = abandonedLimit;

    /// <summary>Renders that have been given up on and have not finished since.</summary>
    public int Abandoned => Volatile.Read(ref _abandoned);

    /// <summary>Records a render that was abandoned.</summary>
    public void Register() => Interlocked.Increment(ref _abandoned);

    /// <summary>Records that an abandoned render eventually finished.</summary>
    public void Release() => Interlocked.Decrement(ref _abandoned);
}
