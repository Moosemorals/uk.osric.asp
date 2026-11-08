// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Net;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;

/// <summary>The owner-only dashboard: queries Prometheus and draws the result as SVG.</summary>
internal static class StatusEndpoint {

    public static async Task HandleAsync(
        HttpContext context,
        Dashboard dashboard,
        CancellationToken cancellationToken) {

        StatusDuration duration = StatusDuration.FromToken(context.Request.Query["duration"]);
        StatusPage page = await dashboard.GetAsync(duration, cancellationToken);

        context.Response.ContentType = "text/html; charset=utf-8";
        SecurityHeaders.NoStore(context);

        await ShellDocument.BeginAsync(context, "Status", stylesheet: "/status.css", cancellationToken: cancellationToken);
        await context.Response.WriteAsync("<h1>Status</h1>", cancellationToken: cancellationToken);
        await WriteNavAsync(context, duration, cancellationToken);

        if (page.Banner is { } banner) {
            await context.Response.WriteAsync($"<p class=\"banner\">{WebUtility.HtmlEncode(banner)}</p>", cancellationToken: cancellationToken);
        }

        foreach (StatusPanel panel in page.Panels) {
            await context.Response.WriteAsync("<section class=\"panel\">", cancellationToken: cancellationToken);
            await context.Response.WriteAsync($"<h2>{WebUtility.HtmlEncode(panel.Title)}</h2>", cancellationToken: cancellationToken);

            if (panel.Svg is null) {
                await context.Response.WriteAsync($"<p class=\"empty\">No data in the last {duration.Token}.</p>", cancellationToken: cancellationToken);
            } else {
                await context.Response.WriteAsync(panel.Svg, cancellationToken: cancellationToken);
                await WriteLegendAsync(context, panel.Labels);
            }

            await context.Response.WriteAsync("</section>", cancellationToken: cancellationToken);
        }

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    private static Task WriteNavAsync(HttpContext context, StatusDuration current, CancellationToken cancellationToken) {
        // The current window is where you already are, so it is text, not a link to itself.
        NavLink[] links = [.. StatusDuration.All.Select(duration => new NavLink(
            "/status?duration=" + Uri.EscapeDataString(duration.Token),
            duration.Token,
            duration.Token == current.Token))];

        return context.Response.WriteAsync(PageDocument.Nav("Time window", links, "status-nav"), cancellationToken);
    }

    private static async Task WriteLegendAsync(HttpContext context, IReadOnlyList<string> labels) {
        await context.Response.WriteAsync("<ul class=\"chart-legend\" role=\"list\">");

        for (int index = 0; index < labels.Count; index++) {
            string colour = SvgText.SeriesColour(index);
            await context.Response.WriteAsync(
                $"<li><span class=\"swatch {colour}\"></span>{WebUtility.HtmlEncode(labels[index])}</li>");
        }

        await context.Response.WriteAsync("</ul>");
    }
}
