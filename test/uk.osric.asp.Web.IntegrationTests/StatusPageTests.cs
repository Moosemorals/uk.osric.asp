// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

/// <summary>Proves /status is wired into the host and behind the editor's sign-in.</summary>
[TestFixture]
public sealed class StatusPageTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(metricsSource: new FixedMetricsSource());

    [Test]
    public async Task DashboardStylesAreServedSeparatelyFromSharedStyles() {
        using HttpClient client = await CreateSignedInClientAsync();

        string status = await client.GetStringAsync("/status");
        using HttpResponseMessage stylesheet = await client.GetAsync("/status.css");
        string statusStyles = await stylesheet.Content.ReadAsStringAsync();
        string sharedStyles = await client.GetStringAsync("/site.css");
        using HttpClient anonymous = CreateClient();
        string login = await anonymous.GetStringAsync("/login");
        // Every stored page is no-store, whoever asks, because a page can keep per-visitor state.
        using HttpResponseMessage anonymousStylesheet = await anonymous.GetAsync("/status.css");

        Assert.That(status, Does.Contain("<link rel=\"stylesheet\" href=\"/status.css\">"));
        Assert.That(status.IndexOf("/site.css", StringComparison.Ordinal),
            Is.LessThan(status.IndexOf("/status.css", StringComparison.Ordinal)), "the view's sheet follows the shared one");
        Assert.That(stylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(stylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(stylesheet.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(anonymousStylesheet.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(anonymousStylesheet.Headers.ETag, Is.Null);
        Assert.That(statusStyles, Does.Contain(".status-nav").And.Contain(".chart").And.Contain(".heatmap"));
        Assert.That(sharedStyles, Does.Contain("body {").And.Contain(".diagnostics"));
        Assert.That(sharedStyles, Does.Not.Contain(".status-nav").And.Not.Contain(".chart").And.Not.Contain(".heatmap"));
        Assert.That(login, Does.Not.Contain("/status.css"));
        Assert.That(login, Does.Contain("<link rel=\"stylesheet\" href=\"/site.css\">"));
    }

    [Test]
    public async Task ChartRedirectsAnAnonymousVisitorToSignIn() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/status/chart?panel=page.render.duration&duration=1h");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
    }

    [Test]
    public async Task TheOwnerFetchesOneChartOnItsOwn() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status/chart?panel=page.render.duration&duration=1h");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("image/svg+xml; charset=utf-8"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(body, Does.StartWith("<svg "));
        Assert.That(body, Does.Not.Contain("<html"), "a chart is a drawing, not a document");
    }

    [Test]
    public async Task StatusRedirectsAnAnonymousVisitorToSignIn() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
        Assert.That(response.Headers.Location?.Query, Does.Contain("returnUrl=%2Fstatus"));
    }

    [Test]
    public async Task StatusRendersTheShellAndEveryPanelForTheOwner() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Status</h1>"));
        Assert.That(body, Does.Contain("<nav class=\"status-nav\" aria-label=\"Time window\">"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Pages</h2>"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Assistant</h2>"));
        Assert.That(body, Does.Contain("<section class=\"status-group\"><h2>Host</h2>"));
        Assert.That(body.Split("<figure class=\"panel\">").Length - 1, Is.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(body, Does.Contain("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"chart\""));
        Assert.That(body, Does.Contain("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"heatmap\""));
    }

    [Test]
    public async Task TheDurationParameterMarksTheCurrentWindow() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status?duration=7d");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">7d</span>"));
        Assert.That(body, Does.Not.Contain("href=\"/status?duration=7d\""));
    }

    [Test]
    public async Task AFortnightOfSamplesStillFitsTheMarkupBudget() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status?duration=30d");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body.Length, Is.LessThan(1_500_000), $"the page is {body.Length} bytes");
    }

    /// <summary>Answers every query with the same two-sample series; there is no Prometheus to ask.</summary>
    private sealed class FixedMetricsSource : IMetricsSource {
        public Task<QueryResult> QueryAsync(
            string query,
            DateTimeOffset from,
            DateTimeOffset to,
            TimeSpan step,
            CancellationToken cancellationToken) {

            return Task.FromResult<QueryResult>(new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(from.AddMinutes(1), 1),
                    new ChartPoint(to, 2),
                ]),
            ]));
        }
    }
}
