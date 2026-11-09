// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

/// <summary>Pins the page's markup budget with a synthetic answer as wide as a window can ask for.</summary>
[TestFixture]
public sealed class StatusPageSizeTests : IntegrationTest {

    private const int Samples = 500;
    private const int HistogramSeries = 15;
    private const int ChartSeries = 8;
    private const int PageBudgetBytes = 2_750_000;

    internal override AspApplication CreateApplication() => new(metricsSource: new WideMetricsSource());

    [Test]
    public async Task ThirtyDaysOfSamplesStillFitTheMarkupBudget() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status?duration=30d");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        Assert.That(body.Length, Is.LessThan(PageBudgetBytes), $"the page is {body.Length} bytes");
        Assert.That(body, Does.Contain("<figure class=\"panel\">"));
    }

    /// <summary>Answers every query with eight series of five hundred samples.</summary>
    private sealed class WideMetricsSource : IMetricsSource {
        public Task<QueryResult> QueryAsync(
            string query,
            DateTimeOffset from,
            DateTimeOffset to,
            TimeSpan step,
            CancellationToken cancellationToken) {

            TimeSpan span = to - from;
            // A histogram answers with one series per le bucket; every other query answers with the
            // eight series a topk or a bounded split can produce.
            int series = query.Contains("by (le)", StringComparison.Ordinal) ? HistogramSeries : ChartSeries;
            List<ChartSeries> answer = [];

            for (int line = 0; line < series; line++) {
                List<ChartPoint> points = [];
                for (int sample = 0; sample < Samples; sample++) {
                    DateTimeOffset at = from + (span * ((double)sample / (Samples - 1)));
                    points.Add(new ChartPoint(at, (sample + line) % 17));
                }

                answer.Add(new ChartSeries("line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture), points));
            }

            return Task.FromResult<QueryResult>(new MetricsMatrix(answer));
        }
    }
}
