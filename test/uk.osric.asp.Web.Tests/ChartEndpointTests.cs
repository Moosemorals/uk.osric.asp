// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class ChartEndpointTests {

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-02T06:00:00Z", CultureInfo.InvariantCulture);

    private static readonly MetricsOptions Options = TestOptions.Metrics with {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    [Test]
    public async Task TheDrawingIsAnSvgThatIsNotCachedByAProxy() {
        (HttpContext context, _) = await GetAsync("?panel=page.render.duration&duration=1h");

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.ContentType, Is.EqualTo("image/svg+xml; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(
            await BodyAsync(context),
            Does.StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" "),
            "the document declares the namespace a browser needs to draw a standalone SVG");
    }

    [Test]
    public async Task TheDrawingIsTheOneThePageEmbeds() {
        FakeMetricsSource source = Answering();
        Dashboard dashboard = New(source);
        ChartWindow window = ChartWindow.At(Now, TimeSpan.FromHours(1));

        (HttpContext context, _) = await GetAsync("?panel=page.cache.lookups&start=2026-11-02T06:00:00Z&duration=60", dashboard, source);
        StatusPage page = await dashboard.GetAsync(window, CancellationToken.None);

        string embedded = page.Panels.Single(panel => panel.Panel.Id == "page.cache.lookups").Svg;
        Assert.That(await BodyAsync(context), Is.EqualTo(embedded));
    }

    [Test]
    public async Task AStartAndADurationNameTheWindow() {
        (HttpContext context, FakeMetricsSource source) = await GetAsync(
            "?panel=page.render.duration&start=2026-11-01T00:00:00Z&duration=90");

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(source.From, Is.EqualTo(DateTimeOffset.Parse("2026-11-01T00:00:00Z", CultureInfo.InvariantCulture)));
        Assert.That(source.To - source.From, Is.EqualTo(TimeSpan.FromMinutes(90)));
    }

    [Test]
    public async Task AnUnknownPanelIsNotFound() {
        (HttpContext context, _) = await GetAsync("?panel=nonsense&duration=1h");

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(await BodyAsync(context), Does.Contain("No panel has that id."));
    }

    [TestCase("?panel=page.render.duration&start=yesterday")]
    [TestCase("?panel=page.render.duration&duration=soon")]
    [TestCase("?panel=page.render.duration&duration=0")]
    public async Task AMalformedWindowIsRefused(string query) {
        (HttpContext context, _) = await GetAsync(query);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public async Task AFailedQueryIsAServiceUnavailable() {
        FakeMetricsSource failing = new(_ => new MetricsUnavailable("Prometheus is unreachable."));
        (HttpContext context, _) = await GetAsync("?panel=page.render.duration&duration=1h", New(failing), failing);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(await BodyAsync(context), Is.EqualTo("Prometheus is unreachable."));
    }

    private static FakeMetricsSource Answering() => new(_ => new MetricsMatrix([
        new ChartSeries("ok", [new ChartPoint(Now.AddMinutes(-30), 1), new ChartPoint(Now, 2)]),
    ]));

    private static Dashboard New(FakeMetricsSource source) =>
        new(new ChartRenderer(source, Options), new TestClock { Now = Now }, TestOptions.Metrics, NullLogger<Dashboard>.Instance);

    private static async Task<(HttpContext Context, FakeMetricsSource Source)> GetAsync(
        string query,
        Dashboard? dashboard = null,
        FakeMetricsSource? source = null) {

        FakeMetricsSource metrics = source ?? Answering();
        HttpContext context = TestHttp.CreateContext(null, "GET", "/status/chart");
        context.Request.QueryString = new QueryString(query);

        await ChartEndpoint.HandleAsync(
            context,
            dashboard ?? New(metrics),
            new TestClock { Now = Now },
            CancellationToken.None);

        return (context, metrics);
    }

    private static async Task<string> BodyAsync(HttpContext context) => await TestHttp.ReadBodyAsync(context);
}
