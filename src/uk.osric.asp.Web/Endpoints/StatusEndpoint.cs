// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using System.Net;
using uk.osric.asp.Web.Metrics;

/// <summary>The owner-only dashboard: queries Prometheus and draws the result as SVG.</summary>
internal static class StatusEndpoint {

    private const int SeriesColours = 5;

    public static async Task HandleAsync(
        HttpContext context,
        MetricsDashboard dashboard,
        CancellationToken cancellationToken) {

        StatusPage page = await dashboard.GetAsync(cancellationToken);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(Framework.GetHeader("Status"), cancellationToken: cancellationToken);
        await context.Response.WriteAsync("<h1>Status</h1>", cancellationToken: cancellationToken);

        if (page.Banner is { } banner) {
            await context.Response.WriteAsync($"<p class=\"banner\">{WebUtility.HtmlEncode(banner)}</p>", cancellationToken: cancellationToken);
        }

        foreach (StatusPanel panel in page.Panels) {
            await context.Response.WriteAsync("<section class=\"panel\">", cancellationToken: cancellationToken);
            await context.Response.WriteAsync($"<h2>{WebUtility.HtmlEncode(panel.Title)}</h2>", cancellationToken: cancellationToken);

            if (panel.Svg is null) {
                await context.Response.WriteAsync("<p class=\"empty\">No data in the last hour.</p>", cancellationToken: cancellationToken);
            } else {
                await context.Response.WriteAsync(panel.Svg, cancellationToken: cancellationToken);
                await WriteLegendAsync(context, panel.Labels);
            }

            await context.Response.WriteAsync("</section>", cancellationToken: cancellationToken);
        }

        await context.Response.WriteAsync(Framework.GetFooter(), cancellationToken: cancellationToken);
    }

    private static async Task WriteLegendAsync(HttpContext context, IReadOnlyList<string> labels) {
        await context.Response.WriteAsync("<ul class=\"chart-legend\">");

        for (int index = 0; index < labels.Count; index++) {
            string colour = "series-" + ((index % SeriesColours) + 1);
            await context.Response.WriteAsync(
                $"<li><span class=\"swatch {colour}\"></span>{WebUtility.HtmlEncode(labels[index])}</li>");
        }

        await context.Response.WriteAsync("</ul>");
    }
}
