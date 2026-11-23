// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The one mapping from the objects that enforce a bound to the figures a page reads.</summary>
/// <remarks>
/// Every figure is asked of the object that would refuse the next attempt, never recomputed from the
/// configuration, so a page's answer cannot disagree with a refusal.
/// </remarks>
internal static class SiteBoundsSnapshot {

    /// <summary>Snapshots what one layer's render has left.</summary>
    /// <param name="fetches">The allowance the whole render shares.</param>
    /// <param name="template">The template-read allowance the whole render shares.</param>
    /// <param name="body">The allowance this layer's writes are charged to.</param>
    /// <param name="embedCapacity">How many embeds the render may make in all.</param>
    /// <param name="embedsMade">How many it has made.</param>
    /// <param name="total">The render's whole budget.</param>
    /// <param name="remaining">What is left of it.</param>
    public static SiteBounds ForRender(
        FetchBudget fetches,
        ReadBudget template,
        BodyBudget body,
        int embedCapacity,
        int embedsMade,
        TimeSpan total,
        TimeSpan remaining) {
        ArgumentNullException.ThrowIfNull(fetches);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(body);

        return new SiteBounds(
            Allowance(fetches),
            Allowance(template),
            new SiteCounter(body.MaxChars, body.Remaining, RenderOptions.MaxBodyCharsKey),
            new SiteCounter(embedCapacity, Math.Max(embedCapacity - embedsMade, 0), RenderOptions.MaxEmbedsKey),
            new SiteTime(total, remaining, RenderOptions.TimeoutSecondsKey));
    }

    /// <summary>Snapshots what one job has left.</summary>
    /// <param name="fetches">The allowance the job holds for its lifetime.</param>
    /// <param name="template">The template-read allowance the job holds for its lifetime.</param>
    /// <param name="total">The job's ceiling.</param>
    /// <param name="remaining">What is left of it.</param>
    /// <remarks>
    /// A job has no response to buffer and cannot compose a page, so it has no body or embed figure.
    /// </remarks>
    public static SiteBounds ForJob(
        FetchBudget fetches,
        ReadBudget template,
        TimeSpan total,
        TimeSpan remaining) {
        ArgumentNullException.ThrowIfNull(fetches);
        ArgumentNullException.ThrowIfNull(template);

        return new SiteBounds(
            Allowance(fetches),
            Allowance(template),
            Body: null,
            Embeds: null,
            Time: new SiteTime(total, remaining, PageJobOptions.MaxDurationKey));
    }

    private static SiteAllowance Allowance(FetchBudget budget) =>
        new(budget.Capacity, budget.Available, budget.PerSecond, budget.Key);

    private static SiteAllowance Allowance(ReadBudget budget) =>
        new(budget.Capacity, budget.Available, budget.PerSecond, budget.Key);
}
