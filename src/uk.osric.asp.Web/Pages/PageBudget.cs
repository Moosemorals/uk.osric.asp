// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;

/// <summary>The process-wide limits on page work, shared by every request.</summary>
/// <remarks>
/// It is a singleton on purpose: a per-request semaphore would limit nothing. Waiting for a render
/// slot is asynchronous, so a queued request holds no thread while it waits.
/// </remarks>
internal sealed class PageBudget : IDisposable {

    /// <summary>The smallest budget a configuration may ask for, in seconds.</summary>
    /// <remarks>
    /// A budget of zero would switch the limit off rather than tighten it, so a value below the floor
    /// is clamped rather than honoured. The floor is not a whole second: the deadline is honoured in
    /// milliseconds, so an operator who wants a tighter bound than the default can ask for one.
    /// </remarks>
    public const double MinimumTimeoutSeconds = 0.05;

    /// <summary>Creates the limits from the configured options.</summary>
    /// <param name="render">The render limits.</param>
    /// <param name="compile">The compile limits.</param>
    public PageBudget(RenderOptions render, CompileOptions compile) {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(compile);

        RenderOptions = render;
        CompileOptions = compile;
        RenderSlots = new SemaphoreSlim(render.Slots, render.Slots);
        CompileSlots = new SemaphoreSlim(compile.Slots, compile.Slots);
    }

    /// <summary>The render limits.</summary>
    public RenderOptions RenderOptions { get; }

    /// <summary>The compile limits.</summary>
    public CompileOptions CompileOptions { get; }

    /// <summary>Describes a budget as the number of seconds a message shows.</summary>
    /// <param name="budget">The budget to describe.</param>
    /// <returns>The seconds without trailing zeros, in the invariant culture.</returns>
    /// <remarks>
    /// A budget may be a fraction of a second, so a message must not print it as a whole number, and
    /// it must not pick up a decimal separator from the server's locale either.
    /// </remarks>
    public static string DescribeSeconds(TimeSpan budget) {
        return budget.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Renders allowed to run at once.</summary>
    public SemaphoreSlim RenderSlots { get; }

    /// <summary>Compiles allowed to run at once.</summary>
    public SemaphoreSlim CompileSlots { get; }

    /// <inheritdoc/>
    public void Dispose() {
        RenderSlots.Dispose();
        CompileSlots.Dispose();
    }
}
