// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Agent;

internal sealed class AgentMetrics : IDisposable {
    public const string MeterName = "uk.osric.asp.Agent";
    public static readonly double[] StepBuckets = [1, 2, 3, 5, 8, 12, 16, 24];
    public static readonly double[] ToolCallBuckets = [0, 1, 2, 4, 8, 16, 32, 64];

    private readonly Meter _meter = new(MeterName, typeof(AgentMetrics).Assembly.GetName().Version?.ToString() ?? "0.0.0");
    private readonly Histogram<double> _runDuration;
    private readonly Histogram<long> _runSteps;
    private readonly Histogram<long> _runToolCalls;
    private readonly Histogram<double> _providerFirstToken;
    private readonly Histogram<double> _providerRequest;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _tokens;

    public AgentMetrics() {
        _runDuration = _meter.CreateHistogram<double>("agent.run.duration", "s");
        _runSteps = _meter.CreateHistogram<long>("agent.run.steps", "{step}");
        _runToolCalls = _meter.CreateHistogram<long>("agent.run.tool_calls", "{call}");
        _providerFirstToken = _meter.CreateHistogram<double>("agent.provider.first_token.duration", "s");
        _providerRequest = _meter.CreateHistogram<double>("agent.provider.request.duration", "s");
        _toolDuration = _meter.CreateHistogram<double>("agent.tool.duration", "s");
        _tokens = _meter.CreateCounter<long>("agent.tokens", "{token}");
    }

    public void RecordRun(string outcome, string model, TimeSpan duration, int steps, int toolCalls) {
        TagList runTags = new() { { "outcome", outcome }, { "model", model } };
        TagList outcomeTags = new() { { "outcome", outcome } };
        _runDuration.Record(duration.TotalSeconds, runTags);
        _runSteps.Record(steps, outcomeTags);
        _runToolCalls.Record(toolCalls, outcomeTags);
    }

    public void RecordProviderFirstToken(string model, TimeSpan duration) =>
        _providerFirstToken.Record(duration.TotalSeconds, new TagList { { "model", model } });

    public void RecordProviderRequest(string model, string outcome, TimeSpan duration) =>
        _providerRequest.Record(duration.TotalSeconds, new TagList { { "model", model }, { "outcome", outcome } });

    public void RecordTool(string tool, bool success, TimeSpan duration) =>
        _toolDuration.Record(duration.TotalSeconds, new TagList {
            { "tool", tool },
            { "outcome", success ? "ok" : "error" },
        });

    public void RecordUsage(AgentUsage usage) {
        if (usage.PromptTokens is > 0) {
            _tokens.Add(usage.PromptTokens.Value, new TagList { { "kind", "prompt" } });
        }
        if (usage.CompletionTokens is > 0) {
            _tokens.Add(usage.CompletionTokens.Value, new TagList { { "kind", "completion" } });
        }
    }

    public void Dispose() => _meter.Dispose();
}
