// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class StatusEndpointTests {

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-02T06:00:00Z", CultureInfo.InvariantCulture);

    private static readonly MetricsOptions Options = new() {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    [Test]
    public async Task TheDocumentIsTheBuildersOwn() {
        string body = await RenderAsync("");

        string expectedStart = PageDocument.Head("Status", ["/status.css"])
            + PageDocument.Banner(SiteNavigation.Nav(authenticated: false, assistantEnabled: false, "/status", string.Empty, "/pages"))
            + PageDocument.OpenMain();
        string expectedEnd = PageDocument.CloseMain() + PageDocument.End();

        Assert.That(body, Does.StartWith(expectedStart), "the head, menu and landmark come from the document tools");
        Assert.That(body, Does.EndWith(expectedEnd));
    }

    [Test]
    public async Task ThePageGroupsItsPanelsUnderHeadings() {
        string body = await RenderAsync("");

        Assert.That(body, Does.Contain("<h1>Status</h1>"));
        Assert.That(body, Does.Contain("<nav class=\"status-nav\" aria-label=\"Time window\">"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Pages</h2>"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Assistant</h2>"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Host</h2>"));
    }

    [Test]
    public async Task EveryPanelIsAFigureWhoseTitlesLinkToTheChart() {
        string body = await RenderAsync("");

        Assert.That(Count(body, "<figure class=\"panel\">"), Is.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(Count(body, "class=\"chart-title-link\""), Is.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(Count(body, "href=\"/status/chart?panel="), Is.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(body, Does.Not.Contain("<figcaption>"));
    }

    [Test]
    public async Task EveryDrawingAndPanelHasAUniqueId() {
        string body = await RenderAsync("");

        string[] ids = [.. AttributeValues(body, "id")];

        Assert.That(ids, Is.Not.Empty);
        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length), "a duplicate id would pin the wrong series");
    }

    [Test]
    public async Task TheNavLinksEveryWindowButTheCurrentOne() {
        string body = await RenderAsync("duration=7d");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">7d</span>"));
        Assert.That(body, Does.Contain("href=\"/status?duration=1h\""));
        Assert.That(body, Does.Contain("href=\"/status?duration=30d\""));
        Assert.That(body, Does.Not.Contain("href=\"/status?duration=7d\""));
    }

    [Test]
    public async Task AnUnknownDurationIsTheDefaultWindow() {
        string body = await RenderAsync("duration=2w");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">1d</span>"));
    }

    [Test]
    public async Task AFailureIsABannerRatherThanABrokenPage() {
        string body = await RenderAsync("", _ => new MetricsUnavailable("Prometheus is unreachable."));

        Assert.That(body, Does.Contain("<p class=\"banner\">Prometheus is unreachable.</p>"));
        Assert.That(body, Does.Not.Contain("<figure class=\"panel\">"));
    }

    [Test]
    public async Task AnEmptyAnswerStillDrawsEveryPanel() {
        string body = await RenderAsync("", _ => new MetricsMatrix([]));

        Assert.That(Count(body, "<figure class=\"panel\">"), Is.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(body, Does.Contain(">No data</text>"));
    }

    [Test]
    public async Task EveryLabelIsEscaped() {
        string body = await RenderAsync("", query => query.Contains("page_storage_payload_size_bytes", StringComparison.Ordinal)
            ? new MetricsMatrix([new ChartSeries("<script>", [new ChartPoint(Now, 1)])])
            : new MetricsMatrix([new ChartSeries("ok", [new ChartPoint(Now, 1)])]));

        Assert.That(body, Does.Contain("&lt;script&gt;"));
        Assert.That(body, Does.Not.Contain("<script>"));
    }

    private static async Task<string> RenderAsync(string query, Func<string, QueryResult>? respond = null) {
        Dashboard dashboard = new(
            new ChartRenderer(
                new FakeMetricsSource(respond ?? (_ => new MetricsMatrix([
                    new ChartSeries("ok", [new ChartPoint(Now.AddMinutes(-30), 1), new ChartPoint(Now, 2)]),
                ]))),
                Options),
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/status");
        context.Request.QueryString = new QueryString(query.Length == 0 ? string.Empty : "?" + query);

        await StatusEndpoint.HandleAsync(context, dashboard, new TestClock { Now = Now }, CancellationToken.None);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static IEnumerable<string> AttributeValues(string markup, string name) {
        string prefix = name + "=\"";
        int index = 0;

        while ((index = markup.IndexOf(prefix, index, StringComparison.Ordinal)) >= 0) {
            int start = index + prefix.Length;
            int end = markup.IndexOf('"', start);
            yield return markup[start..end];
            index = end;
        }
    }

    private static int Count(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;
}
