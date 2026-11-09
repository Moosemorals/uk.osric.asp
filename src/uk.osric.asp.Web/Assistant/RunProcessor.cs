// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Diagnostics;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Logging;

/// <summary>Runs one queued assistant run to completion, streaming its events and executing its tools.</summary>
internal sealed class RunProcessor(
    IServiceScopeFactory scopeFactory,
    IChatCompletionClient client,
    PageToolExecutor tools,
    RunRegistry registry,
    ToolApprovalCoordinator approvals,
    RunContinuationCoordinator continuation,
    AssistantMetrics metrics,
    AssistantOptions options,
    ILogger<RunProcessor> logger) {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(QueuedRun queued, CancellationToken hostStopping) {
        // The run's ids are the worker's scope: it is open before the processor is resolved and
        // outlives this call, so opening them again here would put two identical objects on every
        // line. The model is the one fact only this class learns, so it opens a scope of its own -
        // which every line below, and the provider client's, then carries.
        LogScope modelScope = new();
        using IDisposable? scope = logger.BeginScope(modelScope);

        long runStart = Stopwatch.GetTimestamp();
        RunStream? stream = registry.Find(queued.RunId);
        if (stream is null) {
            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "interrupted", "missing_journal", "The run could not be resumed."));
            return;
        }

        using CancellationTokenSource runTimeout = CancellationTokenSource.CreateLinkedTokenSource(hostStopping, stream.StopToken);
        TimeSpan runInactivity = TimeSpan.FromSeconds(options.RunTimeoutSeconds);
        runTimeout.CancelAfter(runInactivity);

        // The run deadline measures the provider going quiet, so our own work must not count towards
        // it: a tool call is bounded by ToolTimeoutSeconds and an approval by its owner, and neither
        // is the run falling silent.
        void PauseRunTimeout() => runTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        void RestartRunTimeout() => runTimeout.CancelAfter(runInactivity);
        async Task<T> WhilePaused<T>(Func<Task<T>> action) {
            PauseRunTimeout();
            try {
                return await action();
            } finally {
                RestartRunTimeout();
            }
        }

        // Stops the run on a budget it has spent, and returns the limit a continuation grants. The
        // transcript is deliberately untouched: no message is written for the pause, so the next
        // request is the one the run would have made and the model never learns it was interrupted. The
        // run's silence clock is stopped for the wait, because waiting for the owner is not the provider
        // going quiet. The store is the record the reloaded page reads, and the arithmetic here is the
        // one the store applied, so the two agree.
        Task<int> PauseAsync(
            QueuedRun queued,
            RunStream stream,
            string budget,
            int used,
            int limit,
            int window,
            CancellationToken cancellationToken) => WhilePaused(async () => {
                string message = RunBudget.Sentence(budget, limit, window);
                await StoreAsync(store => store.PauseRunAsync(queued.RunId, budget, cancellationToken));
                stream.Publish("run.paused", new { runId = queued.RunId, budget, used, limit, window, message });
                logger.LogInformation("Assistant run {RunId} paused on its {Budget} budget after {Used} of {Limit}",
                    queued.RunId, budget, used, limit);

                await continuation.WaitAsync(queued.RunId, cancellationToken);

                int next = limit + window;
                stream.Publish("run.resumed", new { runId = queued.RunId, budget, limit = next });
                logger.LogInformation("Assistant run {RunId} continued with {Budget} limit {Limit}", queued.RunId, budget, next);

                return next;
            });

        ChatStreamAccumulator? partial = null;
        int step = 1;
        string? activeToolCallId = null;
        string? activeToolName = null;

        try {
            Run? run = await StoreAsync(store => store.GetActiveRunAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
            if (run is null || run.Id != queued.RunId) {
                await FailAsync(queued, stream, "run_missing", "The run is no longer active.", partial, step, CancellationToken.None);
                return;
            }

            stream.StopToken.ThrowIfCancellationRequested();

            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "running", cancellationToken: runTimeout.Token));
            stream.Publish("run.started", new { runId = queued.RunId, model = run.Model, promptVersion = run.PromptVersion });
            modelScope.Set("Model", run.Model);
            logger.LogInformation("Assistant run {RunId} in conversation {ConversationId} started with model {Model}",
                queued.RunId, queued.ConversationId, run.Model);

            // The limits a run starts with, and the ones a continuation widens: the stored value is null
            // until the owner continues, which keeps a run started before this feature meaningful.
            int stepLimit = run.StepLimit ?? options.MaxSteps;
            int toolCallLimit = run.ToolCallLimit ?? options.MaxToolCalls;
            int perStepLimit = options.MaxToolCallsPerStep;
            int toolCallCount = 0;
            int toolCallsRecorded = 0;
            int? completionTokens = null;
            BudgetSnapshot budgets = BudgetSnapshot.Empty;
            while (true) {
                for (; step <= stepLimit; step++) {
                    IReadOnlyList<Message> transcript = await StoreAsync(
                        store => store.GetMessagesAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
                    List<ChatMessage> messages = [new ChatMessage("system", PageAuthorPrompt.Build())];
                    messages.AddRange(transcript.Select(message => message.ToChatMessage()));
                    ContextTrimResult trimmed = ContextTrimmer.Trim(messages, options.MaxContextChars);
                    if (trimmed.Error is not null) {
                        await FailAsync(queued, stream, "context_too_long", trimmed.Error, partial, step, runTimeout.Token);
                        return;
                    }
                    if (trimmed.IsTrimmed) {
                        stream.Publish("run.warning", new {
                            code = "context_trimmed",
                            message = trimmed.TrimmingNote,
                        });
                    }

                    // The step is spent as it starts, and the context row counts the whole conversation
                    // the loop has just read rather than the trimmed request, so it is measured here.
                    budgets = BudgetSnapshot.Measure(messages, step, toolCallsRecorded, completionTokens);
                    stream.Publish("message.started", new { step });
                    PublishBudgets(stream, budgets, stepLimit, toolCallLimit);
                    partial = new ChatStreamAccumulator();
                    ChatRequest request = new(
                        run.Model,
                        trimmed.Messages,
                        PageToolExecutor.Definitions,
                        options.Temperature,
                        options.MaxOutputTokens,
                        options.ReasoningField,
                        options.IncludeUsage);

                    long providerStart = Stopwatch.GetTimestamp();
                    bool firstTokenRecorded = false;
                    string providerOutcome = "ok";
                    try {
                        await foreach (ChatStreamEvent @event in client.StreamAsync(request, runTimeout.Token)) {
                            // The run deadline is an inactivity limit, not a duration: an event the
                            // provider actually streamed re-arms it. The SSE parser drops keep-alive
                            // comments, so a heartbeat cannot keep a stalled run alive.
                            runTimeout.CancelAfter(runInactivity);
                            partial.Add(@event);
                            if (!firstTokenRecorded && @event is ContentDelta or ReasoningDelta or ToolCallDelta) {
                                metrics.RecordProviderFirstToken(run.Model, Stopwatch.GetElapsedTime(providerStart));
                                firstTokenRecorded = true;
                            }
                            switch (@event) {
                                case ContentDelta content:
                                    stream.Publish("content.delta", new { step, text = content.Text });
                                    break;
                                case ReasoningDelta reasoning:
                                    stream.Publish("reasoning.delta", new { step, text = reasoning.Text });
                                    break;
                            }
                        }
                    } catch (ProviderException exception) {
                        providerOutcome = exception.Code;
                        throw;
                    } catch (OperationCanceledException) {
                        providerOutcome = "cancelled";
                        throw;
                    } catch (HttpRequestException exception) {
                        providerOutcome = "provider_unreachable";
                        throw new ProviderException(providerOutcome, "The configured provider could not be reached.", exception);
                    } catch (IOException exception) {
                        providerOutcome = "provider_unreachable";
                        throw new ProviderException(providerOutcome, "The configured provider could not be reached.", exception);
                    } finally {
                        metrics.RecordProviderRequest(run.Model, providerOutcome, Stopwatch.GetElapsedTime(providerStart));
                    }

                    ChatResponse response = partial.Complete();
                    if (response.Usage is { } usage) {
                        metrics.RecordUsage(usage);
                        // The store keeps the last non-null count, so the loop does the same.
                        completionTokens = usage.CompletionTokens ?? completionTokens;
                    }
                    await StoreAsync(store => store.RecordStepAsync(queued.RunId, response.ToolCalls.Count, response.Usage, runTimeout.Token));
                    toolCallsRecorded += response.ToolCalls.Count;
                    if (response.FinishReason == "content_filter") {
                        await PersistAssistantAsync(queued, stream, step, response, "failed", includeToolCalls: false, CancellationToken.None);
                        await FailAsync(queued, stream, "content_filter", "The assistant refused to continue.", null, step, runTimeout.Token);
                        return;
                    }
                    if (!response.HasContent) {
                        await FailAsync(queued, stream, "empty_response", "The provider returned an empty response.", null, step, runTimeout.Token);
                        return;
                    }

                    // The provider reports usage once per step, so this is the only point the output row
                    // can move, and the stored counters are what the reload after the run will show.
                    budgets = budgets with {
                        Steps = step,
                        ToolCalls = toolCallsRecorded,
                        CompletionTokens = completionTokens ?? 0,
                    };
                    PublishBudgets(stream, budgets, stepLimit, toolCallLimit);

                    if (response.FinishReason == "length") {
                        stream.Publish("run.warning", new { code = "length", message = "The response reached the provider token limit." });
                    }

                    if (response.ToolCalls.Count == 0) {
                        // A message that stopped at the token limit is stored as truncated, so the reason
                        // survives the reload that the warning in the status line does not.
                        string status = response.FinishReason == "length" ? "truncated" : "complete";
                        await PersistAssistantAsync(queued, stream, step, response, status, includeToolCalls: false, runTimeout.Token);
                        partial = null;
                        await CompleteAsync(queued, stream, step, response, budgets, stepLimit, toolCallLimit);
                        return;
                    }

                    IReadOnlyList<PreparedToolCall> calls = PrepareToolCalls(response.ToolCalls, step);
                    await PersistAssistantCallsAsync(queued, stream, step, response, calls, runTimeout.Token);
                    partial = null;
                    int callsThisStep = 0;
                    foreach (PreparedToolCall call in calls) {
                        callsThisStep++;
                        activeToolCallId = call.Id;
                        activeToolName = call.Name;

                        // A budget that has run out pauses the run before the call starts, so the page never
                        // shows a tool working that the run was not allowed to make.
                        if (callsThisStep > perStepLimit) {
                            perStepLimit = await PauseAsync(
                                queued, stream, RunBudget.ToolCallsPerStep, callsThisStep - 1, perStepLimit,
                                options.MaxToolCallsPerStep, runTimeout.Token);
                        }
                        if (toolCallCount >= toolCallLimit) {
                            toolCallLimit = await PauseAsync(
                                queued, stream, RunBudget.ToolCalls, toolCallCount, toolCallLimit,
                                options.MaxToolCalls, runTimeout.Token);
                        }

                        ToolExecutionResult result;
                        ToolDefinition? definition = PageToolExecutor.Definitions.SingleOrDefault(tool => tool.Name == call.Name);
                        bool requiresApproval = run.ApprovalMode == ApprovalMode.RequireApproval.ToString()
                            && definition?.NeedsApproval == true;
                        stream.Publish("tool.started", new {
                            step,
                            toolCallId = call.Id,
                            name = call.Name,
                            arguments = call.Arguments,
                            summary = $"Using {call.DisplayName}",
                        });

                        // The cost of a call is known before it runs, which matters because an approval
                        // can wait a long time and a slow tool would otherwise hide it.
                        if (call.Arguments.Length > budgets.LongestArguments) {
                            budgets = budgets with { LongestArguments = call.Arguments.Length };
                            PublishBudgets(stream, budgets, stepLimit, toolCallLimit);
                        }

                        long toolStart = Stopwatch.GetTimestamp();
                        // An approval the path policy has already refused is one the owner could only
                        // deny, so the policy decides first and the prompt never appears.
                        ToolExecutionResult? refused = requiresApproval
                            ? tools.RefuseBeforeApproval(call.Name, call.Arguments)
                            : null;
                        if (refused is not null) {
                            result = refused;
                        } else if (requiresApproval) {
                            result = await WhilePaused(() =>
                                ResolveApprovalAsync(queued, stream, step, call, definition!, runTimeout.Token));
                        } else {
                            result = await WhilePaused(() =>
                                ExecuteToolAsync(call, stream, runTimeout.Token));
                        }

                        toolCallCount++;
                        metrics.RecordTool(definition, result.IsSuccess, Stopwatch.GetElapsedTime(toolStart));
                        await StoreAsync(store => store.AppendMessageAsync(
                            queued.ConversationId,
                            queued.RunId,
                            new MessageInput("tool", result.Content, ToolCallId: call.Id, ToolName: call.Name),
                            runTimeout.Token));
                        stream.Publish("tool.finished", new {
                            step,
                            toolCallId = call.Id,
                            name = call.Name,
                            arguments = call.Arguments,
                            ok = result.IsSuccess,
                            summary = result.Summary,
                            content = result.Content,
                            durationMs = (long)Stopwatch.GetElapsedTime(toolStart).TotalMilliseconds,
                        });

                        // The stored result is what the page measures, so a longer one moves the row now.
                        if (result.Content.Length > budgets.LongestResult) {
                            budgets = budgets with { LongestResult = result.Content.Length };
                            PublishBudgets(stream, budgets, stepLimit, toolCallLimit);
                        }
                        activeToolCallId = null;
                        activeToolName = null;
                    }
                }

                // The steps are spent. Waiting for the owner is not the provider going quiet, so the
                // run's silence clock stops while the question is on the page.
                stepLimit = await PauseAsync(
                    queued, stream, RunBudget.Steps, step - 1, stepLimit, options.MaxSteps, runTimeout.Token);
                PublishBudgets(stream, budgets, stepLimit, toolCallLimit);
            }
        } catch (ProviderException exception) {
            // The exception, not just its code: the inner exception is the socket, TLS or parse
            // failure that says which end of the connection was broken.
            logger.LogWarning(exception, "Assistant provider request for run {RunId} failed with {ErrorCode}", queued.RunId, exception.Code);
            await FailAsync(queued, stream, exception.Code, exception.Message, partial, step, CancellationToken.None);
        } catch (OperationCanceledException) {
            string status = hostStopping.IsCancellationRequested ? "interrupted"
                : stream.StopToken.IsCancellationRequested ? "cancelled" : "failed";
            string code = status switch {
                "interrupted" => "restart",
                "cancelled" => "stop",
                _ => "run_timeout",
            };
            string message = status switch {
                "interrupted" => "The host stopped before the run completed.",
                "cancelled" => "The run was stopped.",
                _ => "The run stopped responding for longer than its configured time limit.",
            };

            // A stop and a restart are the site working as told; a run that fell silent is the
            // provider, which we cannot fix and should still hear about.
            if (status == "interrupted") {
                logger.LogInformation("Assistant run {RunId} was interrupted by the host stopping", queued.RunId);
            } else if (status == "cancelled") {
                logger.LogInformation("Assistant run {RunId} was stopped by its owner", queued.RunId);
            } else {
                logger.LogWarning("Assistant run {RunId} fell silent for more than {TimeoutSeconds} s and was given up on",
                    queued.RunId, options.RunTimeoutSeconds);
            }

            await PersistPartialAsync(queued, stream, step, partial, status, CancellationToken.None);
            if (activeToolCallId is not null) {
                await StoreAsync(store => store.AppendMessageAsync(
                    queued.ConversationId,
                    queued.RunId,
                    new MessageInput("tool", ToolFailure(message, MessageText.Sanitize(activeToolName ?? "tool")).Content,
                        ToolCallId: activeToolCallId, ToolName: activeToolName),
                    CancellationToken.None));
            }
            await StoreAsync(store => store.FailPendingApprovalsAsync(queued.RunId, code, CancellationToken.None));
            if (status == "cancelled") {
                await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, status, cancellationToken: CancellationToken.None));
                stream.Publish("run.cancelled", new { runId = queued.RunId });
            } else {
                await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, status, code, message, CancellationToken.None));
                stream.Publish("run.failed", new { runId = queued.RunId, code, message });
            }
        } catch (Exception exception) {
            logger.LogWarning(exception, "Assistant run {RunId} in conversation {ConversationId} failed unexpectedly", queued.RunId, queued.ConversationId);
            await FailAsync(queued, stream, "internal_error", "The run failed unexpectedly.", partial, step, CancellationToken.None);
        } finally {
            try {
                Run? finalRun = await StoreAsync(store => store.GetRunAsync(
                    queued.ConversationId, queued.Owner, queued.RunId, CancellationToken.None));
                metrics.RecordRun(
                    finalRun?.Status ?? "unknown",
                    finalRun?.Model ?? options.Model,
                    Stopwatch.GetElapsedTime(runStart),
                    finalRun?.StepCount ?? 0,
                    finalRun?.ToolCallCount ?? 0);
                logger.LogInformation("Assistant run {RunId} in conversation {ConversationId} finished as {Outcome} after {Steps} steps and {ToolCalls} tool calls",
                    queued.RunId, queued.ConversationId, finalRun?.Status ?? "unknown", finalRun?.StepCount ?? 0, finalRun?.ToolCallCount ?? 0);
            } catch (Exception exception) {
                logger.LogWarning(exception, "Unable to record metrics for assistant run {RunId}", queued.RunId);
            }
            registry.Complete(queued.RunId, queued.ConversationId);
            registry.RemoveExpired();
        }
    }

    private async Task<ToolExecutionResult> ResolveApprovalAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        PreparedToolCall call,
        ToolDefinition definition,
        CancellationToken cancellationToken) {
        using CancellationTokenSource waiterCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ApprovalResolutionResult> wait = approvals.WaitAsync(queued.RunId, call.Id, waiterCancellation.Token);
        bool created = await StoreAsync(store => store.CreateApprovalAsync(queued.RunId, call.Id, cancellationToken));
        if (!created) {
            waiterCancellation.Cancel();
            try {
                await wait;
            } catch (OperationCanceledException) {
            }
            return ToolFailure("This tool call already has an approval record.", $"{call.DisplayName}: approval conflict");
        }

        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "awaiting_approval", cancellationToken: cancellationToken));
        stream.Publish("approval.required", new {
            step,
            toolCallId = call.Id,
            runId = queued.RunId,
            name = call.Name,
            description = definition.Description,
            arguments = call.Arguments,
        });

        ApprovalResolutionResult decision = await wait;
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "running", cancellationToken: cancellationToken));
        stream.Publish("approval.resolved", new { step, toolCallId = call.Id, decision = decision == ApprovalResolutionResult.Approved ? "approved" : "denied" });

        return decision == ApprovalResolutionResult.Approved
            ? await ExecuteToolAsync(call, stream, cancellationToken)
            : ToolFailure("The owner denied this tool call.", $"{call.DisplayName}: denied");
    }

    private async Task<ToolExecutionResult> ExecuteToolAsync(
        PreparedToolCall call,
        RunStream stream,
        CancellationToken cancellationToken) {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ToolTimeoutSeconds));
        try {
            return await tools.ExecuteAsync(call.Name, call.Arguments, timeout.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ToolFailure("The tool exceeded its configured time limit.", $"{call.DisplayName}: timeout");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            logger.LogWarning(exception, "Assistant tool {ToolName} in run {RunId} failed", call.Name, stream.RunId);
            return ToolFailure("The tool could not complete this operation.", $"{call.DisplayName}: failed");
        }
    }

    private async Task PersistAssistantAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        ChatResponse response,
        string status,
        bool includeToolCalls,
        CancellationToken cancellationToken) {
        long? sequence = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new MessageInput("assistant", response.Content, response.Reasoning,
                includeToolCalls ? SerializeToolCalls(response.ToolCalls) : null, Status: status),
            cancellationToken));
        if (sequence is { } messageSequence) {
            stream.Publish("message.completed", new { step, messageId = messageSequence, status, finishReason = response.FinishReason });
        }
    }

    private async Task PersistAssistantCallsAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        ChatResponse response,
        IReadOnlyList<PreparedToolCall> calls,
        CancellationToken cancellationToken) {
        long? sequence = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new MessageInput("assistant", response.Content, response.Reasoning, SerializePreparedToolCalls(calls)),
            cancellationToken));
        if (sequence is { } messageSequence) {
            stream.Publish("message.completed", new { step, messageId = messageSequence, status = "complete", finishReason = response.FinishReason });
        }
    }

    private async Task PersistPartialAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        ChatStreamAccumulator? accumulator,
        string status,
        CancellationToken cancellationToken) {
        if (accumulator is null) {
            return;
        }
        ChatResponse response = accumulator.Complete();
        if (response.Content.Length == 0 && response.Reasoning.Length == 0) {
            return;
        }
        await PersistAssistantAsync(queued, stream, step, response, status, includeToolCalls: false, cancellationToken);
    }

    private async Task FailAsync(
        QueuedRun queued,
        RunStream stream,
        string code,
        string message,
        ChatStreamAccumulator? partial,
        int step,
        CancellationToken cancellationToken) {
        await PersistPartialAsync(queued, stream, step, partial, "failed", cancellationToken);
        await StoreAsync(store => store.FailPendingApprovalsAsync(queued.RunId, code, cancellationToken));
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "failed", code, message, cancellationToken));
        stream.Publish("run.failed", new { runId = queued.RunId, code, message });
    }

    private async Task CompleteAsync(
        QueuedRun queued,
        RunStream stream,
        int steps,
        ChatResponse response,
        BudgetSnapshot budgets,
        int stepLimit,
        int toolCallLimit) {
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "completed"));
        // The page measures the stored transcript, so the last live figure is measured the same way:
        // anything else would make the reload a correction of what a reader just watched. `Publish`
        // throws once the stream is complete, so nothing may follow `run.completed`.
        IReadOnlyList<Message> transcript = await StoreAsync(
            store => store.GetMessagesAsync(queued.ConversationId, queued.Owner, CancellationToken.None));
        List<ChatMessage> messages = [new ChatMessage("system", PageAuthorPrompt.Build())];
        messages.AddRange(transcript.Select(message => message.ToChatMessage()));
        PublishBudgets(
            stream,
            BudgetSnapshot.Measure(messages, steps, budgets.ToolCalls, budgets.CompletionTokens),
            stepLimit,
            toolCallLimit);
        stream.Publish("run.completed", new {
            runId = queued.RunId,
            steps,
            usage = response.Usage,
        });
    }

    // The keys are the row names the page's markup carries, so the browser needs no translation and
    // `message` is absent: the textarea is that row's only source. The limits travel with the figures
    // because a continuation widens one of them, and a live row must not call a spent budget exceeded.
    private static void PublishBudgets(RunStream stream, BudgetSnapshot budgets, int stepLimit, int toolCallLimit) => stream.Publish("budget.updated", new {
        budgets = new Dictionary<string, long>(StringComparer.Ordinal) {
            ["context"] = budgets.ContextChars,
            ["steps"] = budgets.Steps,
            ["tool-calls"] = budgets.ToolCalls,
            ["tool-arguments"] = budgets.LongestArguments,
            ["tool-result"] = budgets.LongestResult,
            ["output"] = budgets.CompletionTokens,
        },
        limits = new Dictionary<string, long>(StringComparer.Ordinal) {
            ["steps"] = stepLimit,
            ["tool-calls"] = toolCallLimit,
        },
    });

    private async Task<T> StoreAsync<T>(Func<IConversationStore, Task<T>> action) {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IConversationStore>());
    }

    private static IReadOnlyList<PreparedToolCall> PrepareToolCalls(IReadOnlyList<ToolCall> calls, int step) =>
        [.. calls.Select(call => new PreparedToolCall(
            call.Id ?? $"missing-{step}-{call.Index}",
            call.Name ?? string.Empty,
            call.Arguments))];

    private static string SerializeToolCalls(IReadOnlyList<ToolCall> calls) =>
        SerializePreparedToolCalls(PrepareToolCalls(calls, 1));

    private static string SerializePreparedToolCalls(IReadOnlyList<PreparedToolCall> calls) => JsonSerializer.Serialize(
        calls.Select(call => new {
            id = call.Id,
            type = "function",
            function = new { name = call.Name, arguments = call.Arguments },
        }), s_jsonOptions);

    private static ToolExecutionResult ToolFailure(string error, string summary) => new(
        false,
        JsonSerializer.Serialize(new { ok = false, data = (object?)null, error }, s_jsonOptions),
        summary);

    private sealed record PreparedToolCall(string Id, string Name, string Arguments) {

        public string DisplayName => MessageText.Sanitize(Name);
    }
}
