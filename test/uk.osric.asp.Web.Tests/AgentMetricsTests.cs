// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Agent;
using uk.osric.asp.Web.Agent;

[TestFixture]
[NonParallelizable]
public sealed class AgentMetricsTests {

    [Test]
    public void RecordsRunProviderToolAndTokenMeasurementsWithBoundedTags() {
        using AgentMetrics metrics = new();
        List<(string Name, string Tags)> measurements = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, current) => {
            if (instrument.Meter.Name == AgentMetrics.MeterName) {
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
        AgentToolDefinition writePage = AgentPageToolExecutor.Definitions.Single(definition => definition.Name == "write_page");
        metrics.RecordTool(writePage, true, TimeSpan.FromMilliseconds(10));
        metrics.RecordTool(null, false, TimeSpan.FromMilliseconds(10));
        metrics.RecordUsage(new AgentUsage(12, 4));

        Assert.That(measurements.Select(item => item.Name), Is.EquivalentTo([
            "agent.run.duration", "agent.run.steps", "agent.run.tool_calls",
            "agent.provider.first_token.duration", "agent.provider.request.duration",
            "agent.tool.duration", "agent.tool.duration", "agent.tokens", "agent.tokens",
        ]));
        Assert.That(
            measurements.Where(item => item.Name == "agent.tool.duration").Select(item => item.Tags),
            Is.EquivalentTo(["tool=write_page,outcome=ok", $"tool={AgentMetrics.UnknownTool},outcome=error"]));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("runId"));
        Assert.That(measurements.Select(item => item.Tags), Has.None.Contain("conversationId"));
    }

    private static string FormatTags(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"));
}
