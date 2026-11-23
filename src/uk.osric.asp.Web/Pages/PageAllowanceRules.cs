// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using uk.osric.asp.Web.Configuration;

/// <summary>Refuses a rate whose burst is too small for anything to be spent.</summary>
/// <remarks>
/// The rate and the render's deadline are read from different sections, so neither reader's own range
/// can express this. A burst of zero would refuse every caller forever rather than bound one, which
/// is the opposite of what a rate is for, and an operator would see a site that starts and then
/// refuses every fetch.
/// </remarks>
internal static class PageAllowanceRules {

    /// <summary>Records a burst below one unit, naming the rate, the deadline and the smallest rate that works.</summary>
    /// <param name="pages">A reader opened on <c>Pages</c>, so the key it reports is the one an operator edits.</param>
    /// <param name="key">The rate's key below <c>Pages</c>, such as <c>Fetch:FetchesPerSecond</c>.</param>
    /// <param name="perSecond">The configured rate.</param>
    /// <param name="render">The render whose deadline is the burst window.</param>
    public static void RequireOneWholeUnit(
        ConfigurationReader pages,
        string key,
        double perSecond,
        RenderOptions render) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(render);

        if (render.BurstFor(perSecond) >= 1) {
            return;
        }

        pages.Fail(key, string.Create(
            CultureInfo.InvariantCulture,
            $"is {perSecond:0.###}, which with Pages:Render:TimeoutSeconds ({PageBudget.DescribeSeconds(render.Timeout)}) "
            + $"is a burst of 0 requests, so nothing could ever be fetched. Set it to at least "
            + $"{1 / render.TimeoutSeconds:0.####}, or raise Pages:Render:TimeoutSeconds"));
    }
}
