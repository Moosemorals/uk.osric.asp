// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Html;

/// <summary>The owner-only dashboard: queries Prometheus and draws the answer as SVG charts.</summary>
/// <remarks>
/// The document around the body is <see cref="ShellDocument"/>'s, so the head, the sheet list, the site
/// menu, the skip link, the landmark and the close cannot drift from the rest of the site. This type
/// writes the heading, the window nav, the banner and one figure per panel, and nothing else.
/// </remarks>
internal static class StatusEndpoint {

    /// <summary>What the dashboard is the status of, in one line under the heading.</summary>
    /// <remarks>Each name links to the project it names, so the line says what the numbers are made of.</remarks>
    private static string HostLine() =>
        "<p class=\"status-host\">An arm64 VM hosted by <a href=\"https://www.hetzner.com/\">Hetzner</a>"
        + " — a CAX21 with 4 vCPUs, 8 GB of RAM and an 80 GB SSD, running"
        + " <a href=\"https://www.debian.org/\">Debian 13</a>. The app and"
        + " <a href=\"https://prometheus.io/\">Prometheus</a> run rootless under"
        + " <a href=\"https://podman.io/\">podman</a>, behind"
        + " <a href=\"https://caddyserver.com/\">Caddy</a>.</p>";

    /// <summary>Answers <c>GET /admin/status</c> for the owner.</summary>
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
        await context.Response.WriteAsync(AdminNavigation.Nav(context), cancellationToken);
        await WriteNavAsync(context, current, cancellationToken);
        await context.Response.WriteAsync(HostLine(), cancellationToken);

        if (page.Banner is { } banner) {
            await context.Response.WriteAsync(
                $"<p class=\"banner\">{WebUtility.HtmlEncode(banner)}</p>",
                cancellationToken);
        }

        List<StatusGroup> groups = [];
        foreach (string name in StatusCatalogue.Groups) {
            List<StatusPanel> panels = [.. page.Panels.Where(panel => panel.Panel.Group == name)];
            if (panels.Count > 0) {
                groups.Add(new(name, panels));
            }
        }

        if (groups.Count > 0) {
            await WriteContentsAsync(context, groups, cancellationToken);
        }

        for (int index = 0; index < groups.Count; index++) {
            await WriteGroupAsync(context, groups[index], isFirst: index == 0, cancellationToken);
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
            "/admin/status?duration=" + Uri.EscapeDataString(token.Token),
            token.Token,
            string.Equals(token.Token, current, StringComparison.Ordinal)))];

        return context.Response.WriteAsync(ShellDocument.Nav("Time window", links, "status-nav"), cancellationToken);
    }

    // The jump bar carries a group's count so a folded group still says how much it holds; an anchor
    // opens the group it names in the browsers that reveal a closed <details> for a fragment.
    private static Task WriteContentsAsync(
        HttpContext context,
        IReadOnlyList<StatusGroup> groups,
        CancellationToken cancellationToken) {

        string links = string.Join(" ", groups.Select(group =>
            $"<a href=\"#{AnchorFor(group.Name)}\">{WebUtility.HtmlEncode(group.Name)} "
            + $"<span class=\"status-toc-count\">{group.Panels.Count}<span class=\"visually-hidden\"> {Plural(group.Panels.Count)}</span></span></a>"));

        return context.Response.WriteAsync(
            $"<nav class=\"status-toc\" aria-label=\"Groups\"><span class=\"status-toc-label\">Jump to</span> {links}</nav>",
            cancellationToken);
    }

    // One group is open at a time: a name makes the details an exclusive accordion where the
    // attribute is known, and the first is open so the page never arrives with nothing to read.
    private static async Task WriteGroupAsync(
        HttpContext context,
        StatusGroup group,
        bool isFirst,
        CancellationToken cancellationToken) {

        await context.Response.WriteAsync(
            $"<details class=\"status-group\" name=\"status-group\"{(isFirst ? " open" : string.Empty)}>"
            + $"<summary><h2>{WebUtility.HtmlEncode(group.Name)}</h2>"
            + $"<span class=\"status-count\">{group.Panels.Count} {Plural(group.Panels.Count)}</span></summary>"
            + $"<div class=\"status-panels\" id=\"{AnchorFor(group.Name)}\">",
            cancellationToken);

        foreach (StatusPanel panel in group.Panels) {
            await WriteFigureAsync(context, panel, cancellationToken);
        }

        await context.Response.WriteAsync("</div></details>", cancellationToken);
    }

    private static string AnchorFor(string name) => "status-" + name.Replace(' ', '-').ToLowerInvariant();

    private static string Plural(int count) => count == 1 ? "panel" : "panels";

    /// <summary>One group's panels, in catalogue order.</summary>
    private sealed record StatusGroup(string Name, IReadOnlyList<StatusPanel> Panels);
}
