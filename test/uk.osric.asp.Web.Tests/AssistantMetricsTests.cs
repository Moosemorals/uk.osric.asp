// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Metrics;

[TestFixture]
[NonParallelizable]
public sealed class AssistantMetricsTests {

    [Test]
    public void RecordsRunProviderToolAndTokenMeasurementsWithBoundedTags() {
        using AssistantMetrics metrics = new();
        List<(string Name, string Tags)> measurements = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, current) => {
            if (instrument.Meter.Name == AssistantMetrics.MeterName) {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, FormatTags(tags))));
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, FormatTags(tags))));
        listener.Start();

        metrics.RecordRun("completed", "test-model", TimeSpan.FromSeconds(1), 2, 1);
        metrics.RecordProviderFirstToken("test-model", TimeSpan.FromMilliseconds(20));
        metrics.RecordProviderRequest("test-model", "ok", TimeSpan.FromMilliseconds(90));
        ToolDefinition writePage = PageToolExecutor.Definitions.Single(definition => definition.Name == "write_page");
        metrics.RecordTool(writePage, true, TimeSpan.FromMilliseconds(10));
        metrics.RecordTool(null, false, TimeSpan.FromMilliseconds(10));
        metrics.RecordUsage(new TokenUsage(12, 4, 5, 1));
        metrics.RecordMemoryRecall(3, 1_200);
        metrics.RecordMemoryRecall(0, 0);
        metrics.RecordMemoryWrite("remember", MemoryKind.Preference, success: true);
        metrics.RecordMemoryWrite("remember", kind: null, success: false);

        Assert.That(measurements.Select(item => item.Name), Is.EquivalentTo([
            "assistant.run.duration", "assistant.run.steps", "assistant.run.tool_calls",
            "assistant.provider.first_token.duration", "assistant.provider.request.duration",
            "assistant.tool.duration", "assistant.tool.duration",
            "assistant.tokens", "assistant.tokens", "assistant.tokens", "assistant.tokens",
            "assistant.memory.recalled", "assistant.memory.chars", "assistant.memory.recalled", "assistant.memory.chars",
            "assistant.memory.writes", "assistant.memory.writes",
        ]));
        Assert.That(
            measurements.Where(item => item.Name == "assistant.tokens").Select(item => item.Tags),
            Is.EquivalentTo([
                "kind=prompt", "kind=cached", "kind=completion", "kind=reasoning",
            ]));
        Assert.That(
            measurements.Where(item => item.Name == "assistant.tool.duration").Select(item => item.Tags),
            Is.EquivalentTo(["tool=write_page,outcome=ok", $"tool={AssistantMetrics.UnknownTool},outcome=error"]));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("runId"));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("conversationId"));

        // A recall is tagged with whether it found anything, a write with what it did and whether it
        // was allowed; no memory's text, and no id, ever becomes a label.
        Assert.That(
            measurements.Where(item => item.Name == "assistant.memory.recalled").Select(item => item.Tags),
            Is.EquivalentTo(["outcome=hit", "outcome=empty"]));
        Assert.That(
            measurements.Where(item => item.Name == "assistant.memory.writes").Select(item => item.Tags),
            Is.EquivalentTo([
                "op=remember,kind=preference,outcome=ok",
                $"op=remember,kind={AssistantMetrics.NoKind},outcome=refused",
            ]));
    }


    [Test]
    public void ThePublishedInventoryIsTheOneTheMeterRegisters() {
        List<(string Name, InstrumentKind Kind)> published = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, current) => {
            if (instrument.Meter.Name == AssistantMetrics.MeterName) {
                published.Add((instrument.Name, KindOf(instrument)));
            }
        };
        listener.Start();
        using AssistantMetrics metrics = new();
        listener.Dispose();

        Assert.That(
            published.Distinct().OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray(),
            Is.EqualTo(AssistantMetrics.Instruments
                .OrderBy(instrument => instrument.Name, StringComparer.Ordinal)
                .Select(instrument => (instrument.Name, instrument.Kind))
                .ToArray()));
    }

    private static InstrumentKind KindOf(System.Diagnostics.Metrics.Instrument instrument) => instrument switch {
        Histogram<double> or Histogram<long> => InstrumentKind.Histogram,
        Counter<double> or Counter<long> => InstrumentKind.Counter,
        ObservableGauge<double> or ObservableGauge<long> => InstrumentKind.Gauge,
        _ => throw new InvalidOperationException($"{instrument.Name} is a kind the inventory does not know."),
    };

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
