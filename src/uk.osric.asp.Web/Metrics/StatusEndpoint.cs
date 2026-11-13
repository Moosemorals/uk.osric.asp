// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;

/// <summary>The owner-only dashboard: queries Prometheus and draws the answer as SVG charts.</summary>
/// <remarks>
/// The document around the body is <see cref="ShellDocument"/>'s, so the head, the sheet list, the site
/// menu, the skip link, the landmark and the close cannot drift from the rest of the site. This type
/// writes the heading, the window nav, the banner and one figure per panel, and nothing else.
/// </remarks>
internal static class StatusEndpoint {

    /// <summary>Answers <c>GET /status</c> for the owner.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="dashboard">Builds the drawings for the window.</param>
    /// <param name="time">The clock the window ends at.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task HandleAsync(
        HttpContext context,
        Dashboard dashboard,
        TimeProvider time,
        CancellationToken cancellationToken) {

        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(time);

        string? requested = context.Request.Query["duration"];
        string current = ChartWindow.TokenFor(requested);
        ChartWindow window = ChartWindow.FromToken(current, time.GetUtcNow());
        StatusPage page = await dashboard.GetAsync(window, cancellationToken);

        await ShellDocument.BeginAsync(
            context,
            "Status",
            SiteNavigation.For(context),
            stylesheets: ["/status.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync("<h1>Status</h1>", cancellationToken);
        await WriteNavAsync(context, current, cancellationToken);
        await context.Response.WriteAsync(
            """<p>One of the site's own pages will not load? <a href="/admin/restore">Restore every seed</a> from its bundled source.</p>""",
            cancellationToken);

        if (page.Banner is { } banner) {
            await context.Response.WriteAsync(
                $"<p class=\"banner\">{WebUtility.HtmlEncode(banner)}</p>",
                cancellationToken);
        }

        foreach (string group in StatusCatalogue.Groups) {
            StatusPanel[] panels = [.. page.Panels.Where(panel => panel.Panel.Group == group)];
            if (panels.Length == 0) {
                continue;
            }

            await context.Response.WriteAsync(
                $"<section class=\"status-group\"><h2>{WebUtility.HtmlEncode(group)}</h2><div class=\"status-panels\">",
                cancellationToken);

            foreach (StatusPanel panel in panels) {
                await WriteFigureAsync(context, panel, cancellationToken);
            }

            await context.Response.WriteAsync("</div></section>", cancellationToken);
        }

        if (page.Panels.Count == 0 && page.Banner is null) {
            await context.Response.WriteAsync(
                "<p class=\"status-empty\">No panels yet.</p>",
                cancellationToken);
        }

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    private static Task WriteFigureAsync(HttpContext context, StatusPanel panel, CancellationToken cancellationToken) =>
        // The drawing's own title is the link to the chart on its own, so the figure adds nothing.
        context.Response.WriteAsync("<figure class=\"panel\">" + panel.Svg + "</figure>", cancellationToken);

    private static Task WriteNavAsync(HttpContext context, string current, CancellationToken cancellationToken) {
        // The current window is where you already are, so it is text, not a link to itself.
        NavLink[] links = [.. ChartWindow.Tokens.Select(token => new NavLink(
            "/status?duration=" + Uri.EscapeDataString(token.Token),
            token.Token,
            string.Equals(token.Token, current, StringComparison.Ordinal)))];

        return context.Response.WriteAsync(PageDocument.Nav("Time window", links, "status-nav"), cancellationToken);
    }
}
