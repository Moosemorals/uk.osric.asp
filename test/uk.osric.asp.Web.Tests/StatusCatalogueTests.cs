// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class StatusCatalogueTests {

    private static IReadOnlyList<MetricInstrument> Instruments { get; } =
        [.. PageMetrics.Instruments, .. AssistantMetrics.Instruments];

    [Test]
    public void EveryInstrumentHasExactlyOnePanel() {
        string[] panelInstruments = [.. StatusCatalogue.Panels
            .Where(panel => panel.InstrumentName is not null)
            .Select(panel => panel.InstrumentName!)];

        Assert.That(panelInstruments, Is.EquivalentTo(Instruments.Select(instrument => instrument.Name)));
        Assert.That(panelInstruments.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(panelInstruments.Length),
            "an instrument has one panel, not two");
    }

    [Test]
    public void EveryPanelNamesAnInstrumentOrKnownHostFamilies() {
        foreach (Panel panel in StatusCatalogue.Panels) {
            if (panel.InstrumentName is { } name) {
                Assert.That(Instruments.Any(instrument => instrument.Name == name), Is.True, panel.Id);
                Assert.That(panel.HostFamilies, Is.Empty, panel.Id);
            } else {
                Assert.That(panel.HostFamilies, Is.Not.Empty, $"{panel.Id} names neither an instrument nor a family");
            }
        }
    }

    [Test]
    public void TheInstrumentKindDecidesTheChart() {
        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.InstrumentName is not null)) {
            MetricInstrument instrument = Instruments.Single(candidate => candidate.Name == panel.InstrumentName);
            PanelChart expected = instrument.Kind switch {
                InstrumentKind.Histogram => PanelChart.Heatmap,
                InstrumentKind.Counter when panel.PartsOfAWhole is not null => PanelChart.StackedArea,
                InstrumentKind.Counter => PanelChart.Line,
                _ => PanelChart.Line,
            };

            Assert.That(panel.Chart, Is.EqualTo(expected), $"{panel.Id} is a {instrument.Kind}");
        }

        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.Chart == PanelChart.StackedArea)) {
            Assert.That(panel.PartsOfAWhole, Is.Not.Null.And.Not.Empty, $"{panel.Id} stacks without saying what its parts are");
        }
    }

    [Test]
    public void EveryFamilyAQueryNamesIsAnInstrumentOrAKnownRuntimeFamily() {
        foreach (Panel panel in StatusCatalogue.Panels) {
            foreach (PanelQuery query in panel.Queries) {
                foreach (string identifier in Identifiers(query.Query)) {
                    bool known = Instruments.Any(instrument => Families(instrument).Contains(identifier))
                        || panel.HostFamilies.Contains(identifier)
                        || KnownRuntimeFamily(identifier);

                    Assert.That(known, Is.True, $"{panel.Id} names '{identifier}', which is in no inventory: {query.Query}");
                }
            }
        }
    }

    [Test]
    public void AHistogramIsQueriedForItsBucketsAndACounterForItsTotal() {
        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.InstrumentName is not null)) {
            MetricInstrument instrument = Instruments.Single(candidate => candidate.Name == panel.InstrumentName);
            string query = panel.Queries.Single().Query;

            if (instrument.Kind == InstrumentKind.Histogram) {
                Assert.That(query, Does.Contain(PrometheusNames.Buckets(instrument)), panel.Id);
                Assert.That(query, Does.Contain("by (le)"), panel.Id);
            } else if (instrument.Kind == InstrumentKind.Counter) {
                Assert.That(query, Does.Contain(PrometheusNames.Total(instrument)), panel.Id);
            } else {
                Assert.That(query, Does.Contain(PrometheusNames.Family(instrument)), panel.Id);
            }
        }
    }

    [Test]
    public void AGaugeIsQueriedAsALevelRatherThanARate() {
        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.InstrumentName is not null)) {
            MetricInstrument instrument = Instruments.Single(candidate => candidate.Name == panel.InstrumentName);
            if (instrument.Kind != InstrumentKind.Gauge) {
                continue;
            }

            string query = panel.Queries.Single().Query;

            Assert.That(query, Does.Not.Contain("_total"),
                $"{panel.Id} asks for the counter family a gauge does not have");
            Assert.That(query, Does.Not.Contain("rate("), $"{panel.Id} rates a gauge");
            Assert.That(query, Does.Contain(PrometheusNames.Family(instrument)), panel.Id);
        }
    }

    [Test]
    public void EveryHeatmapNamesTheUnitOfItsCells() {
        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.Chart == PanelChart.Heatmap)) {
            Assert.That(panel.CellUnit, Is.Not.Null.And.Not.Empty, $"{panel.Id} has no cell unit");
            Assert.That(panel.CellUnit, Is.Not.EqualTo("per second"),
                $"{panel.Id} says only that its cells are rates, not what they count");
        }
    }

    [Test]
    public void EveryPanelIdIsUniqueAndNamedForItsInstrument() {
        string[] ids = [.. StatusCatalogue.Panels.Select(panel => panel.Id)];

        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
        foreach (Panel panel in StatusCatalogue.Panels.Where(panel => panel.InstrumentName is not null)) {
            Assert.That(panel.Id, Is.EqualTo(panel.InstrumentName), "the chart endpoint addresses the panel by this id");
        }
    }

    [Test]
    public void EveryGroupInTheCatalogueIsListed() {
        string[] groups = [.. StatusCatalogue.Panels.Select(panel => panel.Group).Distinct()];

        Assert.That(groups, Is.EquivalentTo(StatusCatalogue.Groups));
    }

    private static IEnumerable<string> Families(MetricInstrument instrument) => [
        PrometheusNames.Family(instrument),
        PrometheusNames.Buckets(instrument),
        PrometheusNames.Total(instrument),
    ];

    // Function names and keywords are not families; a label inside by (…) or {…} is not one either.
    private static IEnumerable<string> Identifiers(string query) {
        string stripped = StripSelectors(query);

        foreach (string token in stripped.Split([' ', '(', ')', ',', '[', ']', '*', '/', '+', '-', '='], StringSplitOptions.RemoveEmptyEntries)) {
            if (token.All(character => char.IsLetterOrDigit(character) || character is '_' or ':' or '.')
                && !Keywords.Contains(token)) {
                yield return token;
            }
        }
    }

    private static string StripSelectors(string query) {
        System.Text.StringBuilder stripped = new();
        int depth = 0;

        for (int index = 0; index < query.Length; index++) {
            char character = query[index];

            if (character is '(' or '{') {
                depth++;
                continue;
            }

            if (character is ')' or '}') {
                depth--;
                continue;
            }

            if (depth == 0) {
                stripped.Append(character);
            }
        }

        return stripped.ToString();
    }

    private static readonly HashSet<string> Keywords = [
        "sum", "rate", "irate", "avg", "min", "max", "count", "topk", "bottomk", "by", "without", "le",
    ];

    private static bool KnownRuntimeFamily(string identifier) =>
        identifier.StartsWith("dotnet_", StringComparison.Ordinal)
        || identifier.StartsWith("http_server_", StringComparison.Ordinal)
        || identifier.StartsWith("kestrel_", StringComparison.Ordinal);
}
