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
    ];

    /// <summary>Buckets for <c>assistant.run.steps</c>, in steps.</summary>
    /// <remarks>
    /// A run cannot take more steps than <c>Assistant:MaxSteps</c>, and the top boundary is past it, so
    /// the run that exhausted its steps is drawn rather than counted under `+Inf` (DEC-202).
    /// </remarks>
    public static readonly double[] StepBuckets = [1, 2, 3, 5, 8, 12, 16, 24, 32, 48];

    /// <summary>Buckets for <c>assistant.run.tool_calls</c>, in calls.</summary>
    /// <remarks>The top boundary is past <c>Assistant:MaxToolCalls</c> for the same reason (DEC-202).</remarks>
    public static readonly double[] ToolCallBuckets = [0, 1, 2, 4, 8, 16, 32];

    /// <summary>Buckets for <c>assistant.run.duration</c>, in seconds.</summary>
    /// <remarks>
    /// A run's length has no cap: <c>Assistant:RunTimeoutSeconds</c> bounds the provider going quiet,
    /// not the run (DEC-119), so this scale is sized for the work — minutes, up to an hour. The page
    /// scale stops at ten seconds, which is why every run used to land in `+Inf`.
    /// </remarks>
    public static readonly double[] RunDurationBuckets =
        [1, 2.5, 5, 10, 30, 60, 120, 180, 300, 600, 900, 1800, 2700, 3600, 5400];

    /// <summary>Buckets for the provider waits, in seconds: the first token and the whole request.</summary>
    /// <remarks>
    /// Both waits are bounded by the same silence timeout, <c>Assistant:RequestTimeoutSeconds</c>, so
    /// they share one scale whose top boundary is past it (DEC-202): a wait that reached the deadline
    /// is a real bucket, and the row that says so is the one worth reading.
    /// </remarks>
    public static readonly double[] ProviderBuckets =
        [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 240];

    /// <summary>Buckets for <c>assistant.tool.duration</c>, in seconds.</summary>
    /// <remarks>
    /// A tool call is usually a few milliseconds of our own work, and bounded by
    /// <c>Assistant:ToolTimeoutSeconds</c> when it is not; the top boundary is past that cap
    /// (DEC-202).
    /// </remarks>
    public static readonly double[] ToolDurationBuckets =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20];

    /// <summary>The tool tag recorded when a call names no registered tool.</summary>
    /// <remarks>
    /// A tool name is model-supplied, so tagging a made-up or mistyped name verbatim would mint one
    /// time series per value — the same trap as tagging a render that found no page with the path the
    /// caller typed. The transcript still carries the real name; the metric only needs to show that a
    /// call was refused.
    /// </remarks>
    public const string UnknownTool = "(unknown)";

    private readonly Meter _meter = new(MeterName, typeof(AssistantMetrics).Assembly.GetName().Version?.ToString() ?? "0.0.0");
    private readonly Histogram<double> _runDuration;
    private readonly Histogram<long> _runSteps;
    private readonly Histogram<long> _runToolCalls;
    private readonly Histogram<double> _providerFirstToken;
    private readonly Histogram<double> _providerRequest;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _tokens;

    public AssistantMetrics() {
        _runDuration = _meter.CreateHistogram<double>("assistant.run.duration", "s");
        _runSteps = _meter.CreateHistogram<long>("assistant.run.steps", "{step}");
        _runToolCalls = _meter.CreateHistogram<long>("assistant.run.tool_calls", "{call}");
        _providerFirstToken = _meter.CreateHistogram<double>("assistant.provider.first_token.duration", "s");
        _providerRequest = _meter.CreateHistogram<double>("assistant.provider.request.duration", "s");
        _toolDuration = _meter.CreateHistogram<double>("assistant.tool.duration", "s");
        _tokens = _meter.CreateCounter<long>("assistant.tokens", "{token}");
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

    public void Dispose() => _meter.Dispose();
}
