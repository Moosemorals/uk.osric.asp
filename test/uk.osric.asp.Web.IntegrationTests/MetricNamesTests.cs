// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the families the catalogue queries are the ones the exporter writes.</summary>
/// <remarks>
/// <see cref="PrometheusNames"/> derives a family from the instrument's name and unit. This fixture
/// records every instrument once and reads the live <c>/metrics</c> body, so a renamed instrument or a
/// changed unit fails here rather than quietly emptying a panel.
/// </remarks>
[TestFixture]
public sealed class MetricNamesTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new();

    [Test]
    public async Task EveryCatalogueFamilyIsTheOneTheExporterEmits() {
        RecordEveryInstrument();

        using HttpClient client = CreateClient();
        string body = await client.GetStringAsync("/metrics");

        IReadOnlyList<MetricInstrument> instruments = [.. PageMetrics.Instruments, .. AssistantMetrics.Instruments];

        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.InstrumentName is not null)) {
            MetricInstrument instrument = instruments.Single(candidate => candidate.Name == panel.InstrumentName);
            string family = instrument.Kind switch {
                InstrumentKind.Histogram => PrometheusNames.Buckets(instrument),
                InstrumentKind.Counter => PrometheusNames.Total(instrument),
                _ => PrometheusNames.Family(instrument),
            };

            Assert.That(body, Does.Contain(family),
                $"{panel.Id} queries {family}, which the exporter does not write");
        }
    }

    private static void RecordEveryInstrument() {
        PagePath path = PagePath.Normalize("/probe");

        using (PageMetrics pages = new()) {
            pages.RecordRender(path, PageMetrics.RenderOk, PageMetrics.CacheHit, TimeSpan.FromMilliseconds(5));
            pages.RecordWait(TimeSpan.FromMilliseconds(5));
            pages.RecordCompile(path, true, PageMetrics.ReasonSave, TimeSpan.FromMilliseconds(5));
            pages.RecordLoad(path, TimeSpan.FromMilliseconds(5));
            pages.RecordInvoke(path, true, TimeSpan.FromMilliseconds(5));
            pages.RecordSave(path, true, TimeSpan.FromMilliseconds(5));
            pages.RecordFetch(path, PageMetrics.FetchOk, TimeSpan.FromMilliseconds(5));
            pages.RecordImport(PageMetrics.ImportCommitted, TimeSpan.FromMilliseconds(5));
            pages.RecordImageSize(path, 1000);
            pages.RecordAbandoned(path);
            pages.RecordCacheLookup(PageMetrics.LookupHit);
            pages.RecordStaleness(path, PageMetrics.StaleNeverCompiled);
            pages.RecordLayoutFallback(path);
            pages.RecordStorage(1, 2, 3);
        }

        using (AssistantMetrics assistant = new()) {
            assistant.RecordRun("completed", "test-model", TimeSpan.FromSeconds(1), 2, 1);
            assistant.RecordProviderFirstToken("test-model", TimeSpan.FromMilliseconds(20));
            assistant.RecordProviderRequest("test-model", "ok", TimeSpan.FromMilliseconds(90));
            assistant.RecordTool(null, false, TimeSpan.FromMilliseconds(10));
            assistant.RecordUsage(new TokenUsage(12, 4));
        }
    }
}
