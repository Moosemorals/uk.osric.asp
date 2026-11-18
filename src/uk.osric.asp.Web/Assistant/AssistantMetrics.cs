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
        new("assistant.balance", InstrumentKind.Gauge, "{credit}"),
        new("assistant.context.estimate.error", InstrumentKind.Histogram, "{token}"),
        new("assistant.context.shaped", InstrumentKind.Counter, "{step}"),
        new("assistant.budget.exhausted", InstrumentKind.Counter, "{run}"),
        new("assistant.tool.truncated", InstrumentKind.Counter, "{result}"),
    ];

    /// <summary>Buckets for <c>assistant.run.steps</c>, in steps.</summary>
    /// <remarks>
    /// A run cannot take more steps than <c>Assistant:MaxSteps</c>, and the top boundary is past it, so
    /// the run that exhausted its steps is drawn rather than counted under `+Inf` (`metrics-one-panel-per-instrument`).
    /// </remarks>
    public static readonly double[] StepBuckets = [1, 2, 3, 5, 8, 12, 16, 24, 32, 48, 64, 96, 128, 160, 192];

    /// <summary>Buckets for <c>assistant.run.tool_calls</c>, in calls.</summary>
    /// <remarks>The top boundary is past <c>Assistant:MaxToolCalls</c> for the same reason (`metrics-one-panel-per-instrument`).</remarks>
    public static readonly double[] ToolCallBuckets = [0, 1, 2, 4, 8, 16, 32, 64, 128, 192];

    /// <summary>Buckets for <c>assistant.run.duration</c>, in seconds.</summary>
    /// <remarks>
    /// A run's length has no cap: <c>Assistant:RunTimeoutSeconds</c> bounds the provider going quiet,
    /// not the run (`assistant-deadlines-bound-silence`), so this scale is sized for the work — minutes, up to an hour. The page
    /// scale stops at ten seconds, which is why every run used to land in `+Inf`.
    /// </remarks>
    public static readonly double[] RunDurationBuckets =
        [1, 2.5, 5, 10, 30, 60, 120, 180, 300, 600, 900, 1800, 2700, 3600, 5400];

    /// <summary>Buckets for the provider waits, in seconds: the first token and the whole request.</summary>
    /// <remarks>
    /// Both waits are bounded by the same silence timeout, <c>Assistant:RequestTimeoutSeconds</c>, so
    /// they share one scale whose top boundary is past it (`metrics-one-panel-per-instrument`): a wait that reached the deadline
    /// is a real bucket, and the row that says so is the one worth reading.
    /// </remarks>
    public static readonly double[] ProviderBuckets =
        [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 240];

    /// <summary>Buckets for <c>assistant.tool.duration</c>, in seconds.</summary>
    /// <remarks>
    /// A tool call is usually a few milliseconds of our own work, and bounded by
    /// <c>Assistant:ToolTimeoutSeconds</c> when it is not; the top boundary is past that cap
    /// (`metrics-one-panel-per-instrument`).
    /// </remarks>
    public static readonly double[] ToolDurationBuckets =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20];

    /// <summary>Buckets for <c>assistant.context.estimate.error</c>, in tokens.</summary>
    /// <remarks>
    /// The margin is one of the boundaries — 27,525 tokens is three percent of the 917,504-token
    /// prompt budget — so whatever lands above it is the step that spent more than the estimate
    /// allows. The same trick the step and tool-call buckets use: draw the cap rather than hide the
    /// overrun under <c>+Inf</c> (`metrics-one-panel-per-instrument`).
    /// </remarks>
    public static readonly double[] EstimateErrorBuckets = [0, 64, 256, 1024, 4096, 16384, 27525, 65536, 262144];

    /// <summary>Buckets for the memory instruments: how many were recalled and how large they were.</summary>
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

    /// <summary>The ways the planner had to change a request so it would fit.</summary>
    internal static class Shaped {
        public const string Masked = "masked";
        public const string Trimmed = "trimmed";
    }

    /// <summary>The budgets a run can exhaust.</summary>
    internal static class Exhausted {
        public const string Context = "context";
        public const string Provider = "provider";
        public const string Output = "output";
    }

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
    private readonly Histogram<long> _estimateError;
    private readonly Counter<long> _shaped;
    private readonly Counter<long> _exhausted;
    private readonly Counter<long> _truncated;
    private IReadOnlyList<CurrencyCredit>? _balance;

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
        _estimateError = _meter.CreateHistogram<long>("assistant.context.estimate.error", "{token}");
        _shaped = _meter.CreateCounter<long>("assistant.context.shaped", "{step}");
        _exhausted = _meter.CreateCounter<long>("assistant.budget.exhausted", "{run}");
        _truncated = _meter.CreateCounter<long>("assistant.tool.truncated", "{result}");
        _meter.CreateObservableGauge("assistant.balance", ObserveBalance, "{credit}",
            "The provider account's remaining credit, one measurement per currency.");
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
        if (usage.CachedTokens is > 0) {
            _tokens.Add(usage.CachedTokens.Value, new TagList { { "kind", "cached" } });
        }
        if (usage.CompletionTokens is > 0) {
            _tokens.Add(usage.CompletionTokens.Value, new TagList { { "kind", "completion" } });
        }
        if (usage.ReasoningTokens is > 0) {
            _tokens.Add(usage.ReasoningTokens.Value, new TagList { { "kind", "reasoning" } });
        }
    }

    /// <summary>Records how far one step's estimate was from the count the provider reported.</summary>
    /// <param name="estimate">The tokens the step's request was estimated at.</param>
    /// <param name="actual">The prompt tokens the provider counted for it.</param>
    /// <remarks>
    /// The magnitude is the figure; the direction is the label. <c>under</c> is the one that can
    /// breach the budget, so a rising share of it is the signal to revisit the margin.
    /// </remarks>
    public void RecordEstimateError(long estimate, long actual) {
        long error = estimate - actual;
        _estimateError.Record(Math.Abs(error), new TagList { { "direction", error < 0 ? "under" : "over" } });
    }

    /// <summary>Records that the planner had to change a request so it would fit.</summary>
    /// <param name="kind">One of <see cref="Shaped.Masked"/> or <see cref="Shaped.Trimmed"/>.</param>
    public void RecordShaped(string kind) => _shaped.Add(1, new TagList { { "kind", kind } });

    /// <summary>Records that a run stopped on a budget.</summary>
    /// <param name="cause">One of <see cref="Exhausted.Context"/>, <see cref="Exhausted.Provider"/>
    /// or <see cref="Exhausted.Output"/>.</param>
    public void RecordBudgetExhausted(string cause) => _exhausted.Add(1, new TagList { { "cause", cause } });

    /// <summary>Records that one tool result was truncated at its cap.</summary>
    /// <param name="tool">The registered tool's name, or <see cref="UnknownTool"/>.</param>
    public void RecordToolTruncated(string? tool) =>
        _truncated.Add(1, new TagList { { "tool", tool ?? UnknownTool } });

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
            { "kind", kind is { } value ? value.Name() : NoKind },
            { "outcome", success ? "ok" : "refused" },
        });

    /// <summary>Records the provider's credit, one measurement per currency.</summary>
    /// <param name="credits">The currencies the last sample could read.</param>
    public void RecordBalance(IReadOnlyList<CurrencyCredit> credits) {
        ArgumentNullException.ThrowIfNull(credits);

        Volatile.Write(ref _balance, [.. credits]);
    }

    private IEnumerable<Measurement<double>> ObserveBalance() {
        IReadOnlyList<CurrencyCredit>? credits = Volatile.Read(ref _balance);
        if (credits is null) {
            yield break;
        }

        foreach (CurrencyCredit credit in credits) {
            yield return new Measurement<double>(credit.Amount, new TagList { { "currency", credit.Currency } });
        }
    }

    public void Dispose() => _meter.Dispose();
}

/// <summary>One currency's credit, as the balance gauge reports it.</summary>
/// <param name="Currency">The currency code the provider reported.</param>
/// <param name="Amount">The total available credit in that currency.</param>
internal sealed record CurrencyCredit(string Currency, double Amount);
