// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

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
        metrics.RecordUsage(new TokenUsage(12, 4));

        Assert.That(measurements.Select(item => item.Name), Is.EquivalentTo([
            "assistant.run.duration", "assistant.run.steps", "assistant.run.tool_calls",
            "assistant.provider.first_token.duration", "assistant.provider.request.duration",
            "assistant.tool.duration", "assistant.tool.duration", "assistant.tokens", "assistant.tokens",
        ]));
        Assert.That(
            measurements.Where(item => item.Name == "assistant.tool.duration").Select(item => item.Tags),
            Is.EquivalentTo(["tool=write_page,outcome=ok", $"tool={AssistantMetrics.UnknownTool},outcome=error"]));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("runId"));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("conversationId"));
    }

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
