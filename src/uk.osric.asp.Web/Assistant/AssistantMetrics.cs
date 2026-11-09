// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Metrics;

/// <summary>The meter and instruments that describe assistant runs.</summary>
internal sealed class AssistantMetrics : IDisposable {
    public const string MeterName = "uk.osric.asp.Assistant";

    /// <summary>Every instrument this meter registers, in the order the page lists them.</summary>
    /// <remarks>
    /// The catalogue maps each of these to exactly one panel, and <c>AssistantMetricsTests</c> listens
    /// to the meter to prove this list and the registrations below agree.
    /// </remarks>
    public static IReadOnlyList<MetricInstrument> Instruments { get; } = [
        new("assistant.run.duration", InstrumentKind.Histogram, "s"),
        new("assistant.run.steps", InstrumentKind.Histogram, "{step}"),
        new("assistant.run.tool_calls", InstrumentKind.Histogram, "{call}"),
        new("assistant.provider.first_token.duration", InstrumentKind.Histogram, "s"),
        new("assistant.provider.request.duration", InstrumentKind.Histogram, "s"),
        new("assistant.tool.duration", InstrumentKind.Histogram, "s"),
        new("assistant.tokens", InstrumentKind.Counter, "{token}"),
        new("assistant.memory.recalled", InstrumentKind.Histogram, "{memory}"),
        new("assistant.memory.chars", InstrumentKind.Histogram, "{char}"),
        new("assistant.memory.writes", InstrumentKind.Counter, "{write}"),
    ];

    public static readonly double[] StepBuckets = [1, 2, 3, 5, 8, 12, 16, 24];
    public static readonly double[] ToolCallBuckets = [0, 1, 2, 4, 8, 16, 32, 64];
    public static readonly double[] MemoryCountBuckets = [0, 1, 2, 4, 8, 12];
    public static readonly double[] MemoryCharBuckets = [0, 512, 1024, 2048, 4096];

    /// <summary>The tool tag recorded when a call names no registered tool.</summary>
    /// <remarks>
    /// A tool name is model-supplied, so tagging a made-up or mistyped name verbatim would mint one
    /// time series per value — the same trap as tagging a render that found no page with the path the
    /// caller typed. The transcript still carries the real name; the metric only needs to show that a
    /// call was refused.
    /// </remarks>
    public const string UnknownTool = "(unknown)";

    /// <summary>The kind tag recorded for a write that had no kind, which includes every refusal.</summary>
    public const string NoKind = "(none)";

    private readonly Meter _meter = new(MeterName, typeof(AssistantMetrics).Assembly.GetName().Version?.ToString() ?? "0.0.0");
    private readonly Histogram<double> _runDuration;
    private readonly Histogram<long> _runSteps;
    private readonly Histogram<long> _runToolCalls;
    private readonly Histogram<double> _providerFirstToken;
    private readonly Histogram<double> _providerRequest;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _tokens;
    private readonly Histogram<long> _memoryRecalled;
    private readonly Histogram<long> _memoryChars;
    private readonly Counter<long> _memoryWrites;

    public AssistantMetrics() {
        _runDuration = _meter.CreateHistogram<double>("assistant.run.duration", "s");
        _runSteps = _meter.CreateHistogram<long>("assistant.run.steps", "{step}");
        _runToolCalls = _meter.CreateHistogram<long>("assistant.run.tool_calls", "{call}");
        _providerFirstToken = _meter.CreateHistogram<double>("assistant.provider.first_token.duration", "s");
        _providerRequest = _meter.CreateHistogram<double>("assistant.provider.request.duration", "s");
        _toolDuration = _meter.CreateHistogram<double>("assistant.tool.duration", "s");
        _tokens = _meter.CreateCounter<long>("assistant.tokens", "{token}");
        _memoryRecalled = _meter.CreateHistogram<long>("assistant.memory.recalled", "{memory}");
        _memoryChars = _meter.CreateHistogram<long>("assistant.memory.chars", "{char}");
        _memoryWrites = _meter.CreateCounter<long>("assistant.memory.writes", "{write}");
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

    /// <summary>Records one tool call under a registered tool's name, or
    /// <see cref="UnknownTool"/> when <paramref name="tool"/> is null.</summary>
    /// <param name="tool">The definition the call resolved to, or null when it named no registered
    /// tool. A definition rather than a name, so a model-supplied string cannot become a label.</param>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="duration">How long the call took.</param>
    public void RecordTool(ToolDefinition? tool, bool success, TimeSpan duration) =>
        _toolDuration.Record(duration.TotalSeconds, new TagList {
            { "tool", tool?.Name ?? UnknownTool },
            { "outcome", success ? "ok" : "error" },
        });

    public void RecordUsage(TokenUsage usage) {
        if (usage.PromptTokens is > 0) {
            _tokens.Add(usage.PromptTokens.Value, new TagList { { "kind", "prompt" } });
        }
        if (usage.CompletionTokens is > 0) {
            _tokens.Add(usage.CompletionTokens.Value, new TagList { { "kind", "completion" } });
        }
    }

    /// <summary>Records what one run recalled: how many memories and how many characters.</summary>
    /// <param name="memories">The memories the run was given.</param>
    /// <param name="characters">What the block cost.</param>
    public void RecordMemoryRecall(int memories, int characters) {
        TagList tags = new() { { "outcome", memories > 0 ? "hit" : "empty" } };
        _memoryRecalled.Record(memories, tags);
        _memoryChars.Record(characters, tags);
    }

    /// <summary>Records one memory write.</summary>
    /// <param name="operation">The tool: <c>remember</c>, <c>update</c> or <c>forget</c>.</param>
    /// <param name="kind">The kind written, or null for a refusal, where the kind may be what was
    /// wrong: <see cref="NoKind"/> is the tag instead.</param>
    /// <param name="success">Whether the store changed.</param>
    public void RecordMemoryWrite(string operation, MemoryKind? kind, bool success) =>
        _memoryWrites.Add(1, new TagList {
            { "op", operation },
            { "kind", kind is { } value ? MemoryKinds.Name(value) : NoKind },
            { "outcome", success ? "ok" : "refused" },
        });

    public void Dispose() => _meter.Dispose();
}
