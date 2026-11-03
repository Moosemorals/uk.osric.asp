// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

/// <summary>Proves /status is wired into the host and behind the editor's sign-in.</summary>
[TestFixture]
public sealed class StatusPageTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(metricsSource: new FixedMetricsSource());

    [Test]
    public async Task StatusRedirectsAnAnonymousVisitorToSignIn() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
        Assert.That(response.Headers.Location?.Query, Does.Contain("returnUrl=%2Fstatus"));
    }

    [Test]
    public async Task StatusRendersChartsForTheOwner() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Status</h1>"));
        Assert.That(body, Does.Contain("<svg class=\"chart\""));
    }

    [Test]
    public async Task TheDurationParameterWidensTheCharts() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status?duration=7d");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">7d</span>"));
        Assert.That(body, Does.Contain("viewBox=\"0 0 1040 200\""));
    }

    /// <summary>Answers every query with the same two-sample series; there is no Prometheus to ask.</summary>
    private sealed class FixedMetricsSource : IMetricsSource {
        public Task<MetricsQueryResult> QueryAsync(
            string query,
            DateTimeOffset from,
            DateTimeOffset to,
            TimeSpan step,
            CancellationToken cancellationToken) {

            return Task.FromResult<MetricsQueryResult>(new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(from.AddMinutes(1), 1),
                    new ChartPoint(to, 2),
                ]),
            ]));
        }
    }
}
