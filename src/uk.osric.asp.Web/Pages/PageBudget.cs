// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The process-wide limits on page work, shared by every request.</summary>
/// <remarks>
/// It is a singleton on purpose: a per-request semaphore would limit nothing. Waiting for a render
/// slot is asynchronous, so a queued request holds no thread while it waits.
/// </remarks>
public sealed class PageBudget : IDisposable {

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
