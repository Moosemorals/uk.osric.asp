// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Diagnostics;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant.Tools;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Logging;
using uk.osric.asp.Web.Users;

/// <summary>Runs one queued assistant run to completion, streaming its events and executing its tools.</summary>
internal sealed class RunProcessor(
    IServiceScopeFactory scopeFactory,
    IChatCompletionClient client,
    PageToolExecutor tools,
    RunRegistry registry,
    OwnerInterruptCoordinator interrupts,
    AssistantMetrics metrics,
    AssistantOptions options,
    ModelWindowSampler windows,
    IUserStore users,
    ViewTokenStore views,
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
            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, RunStatus.Interrupted, "missing_journal", "The run could not be resumed."));
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
        // request is the one the run would have made and the model never learns it was interrupted.
        // The run's silence clock is stopped for the wait, because waiting for the owner is not the
        // provider going quiet. The store is the record the reloaded page reads, and the arithmetic
        // here is the one the store applied, so the two agree.
        Task<int> PauseAsync(
            QueuedRun queued,
            RunStream stream,
            int step,
            string budget,
            int used,
            int limit,
            int window,
            CancellationToken cancellationToken) => WhilePaused(async () => {
                string message = RunBudget.Sentence(budget, limit, window);
                OwnerInterrupt? interrupt = await WaitAsync(
                    queued, stream, step, OwnerInterruptKind.Continuation, toolCallId: null,
                    new ContinuationRequest(budget, limit, window, message), cancellationToken);
                if (interrupt is null) {
                    // The run was not running when the ask was opened, which only happens when it has
                    // already been stopped; the loop's own cancellation ends it.
                    throw new InvalidOperationException("The run stopped before it could wait on its owner.");
                }

                int next = limit + window;
                logger.LogInformation("Assistant run {RunId} continued with {Budget} limit {Limit}", queued.RunId, budget, next);

                return next;
            });

        ChatStreamAccumulator? partial = null;

        // One request, streamed into the accumulator the transcript and the page both read. It is a
        // function rather than inline so a rejected request can be planned again and sent once more.
        async Task<ChatResponse> StreamStepAsync(ChatRequest stepRequest, int stepNumber) {
            ChatStreamAccumulator accumulator = new();
            partial = accumulator;

            long providerStart = Stopwatch.GetTimestamp();
            bool firstTokenRecorded = false;
            string providerOutcome = "ok";
            try {
                await foreach (ChatStreamEvent @event in client.StreamAsync(stepRequest, runTimeout.Token)) {
                    // The run deadline is an inactivity limit, not a duration: an event the provider
                    // actually streamed re-arms it. The SSE parser drops keep-alive comments, so a
                    // heartbeat cannot keep a stalled run alive.
                    runTimeout.CancelAfter(runInactivity);
                    accumulator.Add(@event);
                    if (!firstTokenRecorded && @event is ContentDelta or ReasoningDelta or ToolCallDelta) {
                        metrics.RecordProviderFirstToken(stepRequest.Model, Stopwatch.GetElapsedTime(providerStart));
                        firstTokenRecorded = true;
                    }
                    switch (@event) {
                        case ContentDelta content:
                            stream.Publish("content.delta", new { step = stepNumber, text = content.Text });
                            break;
                        case ReasoningDelta reasoning:
                            stream.Publish("reasoning.delta", new { step = stepNumber, text = reasoning.Text });
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
                metrics.RecordProviderRequest(stepRequest.Model, providerOutcome, Stopwatch.GetElapsedTime(providerStart));
            }

            return accumulator.Complete();
        }

        // The estimate's error is remembered across steps: it is what the panel's Estimate row and the
        // error gauge both read, and a reload rebuilds it from the run's stored figures.
        long estimateError = 0;
        int lastMaxTokens = 0;
        bool cacheMissReported = false;
        int step = 1;
        string? activeToolCallId = null;
        string? activeToolName = null;
        // Recall happens once, on the first step, and the block is frozen for the rest of the run: a
        // memory the model writes mid-run is visible to the next run, not this one.
        string memoryBlock = string.Empty;

        // The run's view of the site: one token, for the owner the run already acts as, revoked in the
        // finally below so every exit - a finish, a failure, a cancel, an exception - takes it with it.
        // Nothing stores it, so its lifetime is the run's rather than a second one to reason about.
        LocalUser? owner = await users.FindAsync(queued.Owner, runTimeout.Token);
        ViewToken view = views.Mint(queued.RunId, queued.Owner, owner?.SecurityStamp);

        try {
            Run? run = await StoreAsync(store => store.GetActiveRunAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
            if (run is null || run.Id != queued.RunId) {
                await FailAsync(queued, stream, "run_missing", "The run is no longer active.", partial, step, CancellationToken.None);
                return;
            }

            stream.StopToken.ThrowIfCancellationRequested();

            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, RunStatus.Running, cancellationToken: runTimeout.Token));
            stream.Publish("run.started", new { runId = queued.RunId, model = run.Model, promptVersion = run.PromptVersion });
            modelScope.Set("Model", run.Model);
            logger.LogInformation("Assistant run {RunId} in conversation {ConversationId} started with model {Model}",
                queued.RunId, queued.ConversationId, run.Model);

            // The window is the whole budget, and a guessed one is what the budget's decision refuses:
            // a run without it says so rather than planning against a number nobody reported.
            ModelWindowRead budgetRead = windows.Read;
            if (windows.Budget is not { } budget) {
                await FailAsync(queued, stream, "model_window_unknown",
                    "The provider has not reported this model's context window, so the run cannot be planned.",
                    null, 1, CancellationToken.None);
                return;
            }

            // A reloaded page rebuilds the last step's error from the figures the run stored, so the row
            // says the same thing before and after a refresh.
            if (run.EstimatedTokens is { } storedEstimate && run.LastPromptTokens is { } storedPrompt) {
                estimateError = storedEstimate - storedPrompt;
            }
            lastMaxTokens = run.MaxTokens ?? 0;

            int toolCallCount = 0;
            RunUsage usageTotals = run.ToUsage();
            TokenCalibration? calibration = run.RequestBytes is { } calibratedBytes && run.LastPromptTokens is { } calibratedPrompt
                ? new TokenCalibration(calibratedBytes, calibratedPrompt)
                : null;
            BudgetSnapshot budgets = BudgetSnapshot.Empty;
            // The warnings describe what the run as a whole did, so each is published at most once:
            // a hundred steps that all masked an old result are one fact, not a hundred.
            bool maskedWarningSent = false;
            bool trimmedWarningSent = false;
            ToolContext toolContext = new(queued.Owner, queued.ConversationId, queued.RunId);
            // The limits a run starts with, and the ones a continuation widens: the stored value is
            // null until the owner continues, which keeps a run started before this feature meaningful.
            int stepLimit = run.StepLimit ?? options.MaxSteps;
            int toolCallLimit = run.ToolCallLimit ?? options.MaxToolCalls;
            int perStepLimit = options.MaxToolCallsPerStep;
            while (true) {
                for (; step <= stepLimit; step++) {
                    IReadOnlyList<Message> transcript = await StoreAsync(
                        store => store.GetMessagesAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
                    if (step == 1) {
                        memoryBlock = await RecallMemoryAsync(queued, stream, transcript, budget, runTimeout.Token);
                    }
                    string prompt = PageAuthorPrompt.Build();
                    string systemPrompt = memoryBlock.Length == 0 ? prompt : $"{prompt}\n\n{memoryBlock}";
                    List<ChatMessage> messages = [new ChatMessage("system", systemPrompt)];
                    messages.AddRange(transcript.Select(message => message.ToChatMessage()));
                    ChatRequest request = new(
                        run.Model,
                        messages,
                        PageToolExecutor.Definitions,
                        options.Temperature,
                        budget.MinimumOutputTokens,
                        options.ReasoningField);
                    ContextPlan plan = ContextPlanner.Build(request, budget, TokenEstimator.For(calibration));
                    if (plan.Error is not null) {
                        metrics.RecordBudgetExhausted(AssistantMetrics.ExhaustedContext);
                        await FailAsync(queued, stream, "context_too_long", plan.Error, partial, step, runTimeout.Token);
                        return;
                    }
                    if (plan.IsMasked) {
                        metrics.RecordShaped(AssistantMetrics.ShapedMasked);
                    }
                    if (plan.IsTrimmed) {
                        metrics.RecordShaped(AssistantMetrics.ShapedTrimmed);
                    }
                    if (plan.IsMasked && !maskedWarningSent) {
                        stream.Publish("run.warning", new {
                            code = "context_masked",
                            message = "Earlier tool output and arguments were shortened to fit the request budget.",
                        });
                        maskedWarningSent = true;
                    }
                    if (plan.IsTrimmed && !trimmedWarningSent) {
                        stream.Publish("run.warning", new {
                            code = "context_trimmed",
                            message = "Earlier conversation history was omitted to fit the request budget.",
                        });
                        trimmedWarningSent = true;
                    }
                    if (plan.IsMasked || plan.IsTrimmed) {
                        logger.LogInformation(
                            "Assistant step {Step} shaped the request: {MaskedResults} tool results and {MaskedArguments} arguments elided, {DroppedMessages} messages dropped, {OriginalTokens} estimated tokens became {EstimatedTokens} ({RequestBytes} bytes)",
                            step, plan.MaskedToolResults, plan.MaskedArguments, plan.DroppedMessages,
                            plan.OriginalTokens, plan.EstimatedTokens, plan.RequestBytes);
                    }

                    // The budget row counts the request the planner built, and the counters are what the
                    // loop has already spent, so the live figure is the one the page will reload.
                    budgets = BudgetSnapshot.Measure(
                        plan, messages, step, toolCallCount, usageTotals.LastCompletionTokens, lastMaxTokens,
                        budget, estimateError);
                    stream.Publish("message.started", new { step });
                    PublishBudgets(stream, budgets, usageTotals, budget, stepLimit, toolCallLimit);

                    ChatResponse response;
                    try {
                        response = await StreamStepAsync(plan.Request, step);
                    } catch (ProviderException exception) when (exception.Code == ProviderException.ContextWindowMismatch) {
                        // A rejection for length is the one signal that the cached window is stale, so the
                        // list is read again. Nothing was generated and no tool ran, so a retry costs one
                        // request; a window that has not moved means the model really cannot take it.
                        ModelWindowRead refreshed;
                        try {
                            refreshed = await windows.RefreshAsync(runTimeout.Token);
                        } catch (ConfigurationException configuration) {
                            // The provider has just said the configured model does not exist, which is an
                            // operator's problem rather than this run's: it is named the way a run with no
                            // window is named, and the deploy hears the rest at its next restart.
                            logger.LogError(configuration, "The model list no longer contains {Model}", run.Model);
                            metrics.RecordBudgetExhausted(AssistantMetrics.ExhaustedProvider);
                            await FailAsync(queued, stream, "model_window_unknown", configuration.Message,
                                partial, step, runTimeout.Token);
                            return;
                        }
                        if (windows.Budget is not { } updated || refreshed.SameAs(budgetRead)) {
                            metrics.RecordBudgetExhausted(AssistantMetrics.ExhaustedProvider);
                            await FailAsync(queued, stream, exception.Code, exception.Message, partial, step, runTimeout.Token);
                            return;
                        }

                        budget = updated;
                        budgetRead = refreshed;
                        plan = ContextPlanner.Build(request, budget, TokenEstimator.For(calibration));
                        if (plan.Error is not null) {
                            metrics.RecordBudgetExhausted(AssistantMetrics.ExhaustedContext);
                            await FailAsync(queued, stream, "context_too_long", plan.Error, partial, step, runTimeout.Token);
                            return;
                        }
                        logger.LogInformation(
                            "The provider reports a new {ContextWindow} token window for {Model}, so step {Step} was planned again",
                            budget.ContextWindow, run.Model, step);
                        response = await StreamStepAsync(plan.Request, step);
                    }

                    if (response.Usage is { } usage) {
                        metrics.RecordUsage(usage);
                        usageTotals = usageTotals.Add(usage);
                        if (usage.PromptTokens is > 0) {
                            calibration = new TokenCalibration(plan.RequestBytes, usage.PromptTokens.Value);
                            estimateError = plan.EstimatedTokens - usage.PromptTokens.Value;
                            metrics.RecordEstimateError(plan.EstimatedTokens, usage.PromptTokens.Value);
                            logger.LogInformation(
                                "Assistant step {Step}: estimated {Estimate} tokens, the provider counted {Actual} ({Delta} {Direction}); the request was {Shape}",
                                step, plan.EstimatedTokens, usage.PromptTokens.Value, Math.Abs(estimateError),
                                estimateError < 0 ? "under" : "over",
                                plan.IsMasked || plan.IsTrimmed ? "shaped" : "untouched");
                        }

                        // The system prompt and the memory block are constants from the second step on,
                        // so an untouched request the provider has already seen should report most of
                        // its prompt as cached. No cache hit means something moved that should not have.
                        if (!cacheMissReported
                            && step > 1
                            && !plan.IsMasked
                            && !plan.IsTrimmed
                            && (usage.CachedTokens ?? 0) == 0) {
                            cacheMissReported = true;
                            logger.LogWarning(
                                "Assistant step {Step} sent an unchanged request and the provider reported no cached tokens; the prompt prefix is moving between steps",
                                step);
                        }
                    }
                    await StoreAsync(store => store.RecordStepAsync(
                        queued.RunId, plan.RequestBytes, plan.EstimatedTokens, plan.MaxTokens,
                        response.Usage, runTimeout.Token));
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
                    lastMaxTokens = plan.MaxTokens;
                    budgets = budgets with {
                        Steps = step,
                        ToolCalls = toolCallCount,
                        LastCompletionTokens = usageTotals.LastCompletionTokens,
                        LastMaxTokens = lastMaxTokens,
                        EstimateError = estimateError,
                    };
                    PublishBudgets(stream, budgets, usageTotals, budget, stepLimit, toolCallLimit);

                    if (response.FinishReason == "length") {
                        metrics.RecordBudgetExhausted(AssistantMetrics.ExhaustedOutput);
                        // A reply cut through a tool call's arguments is unparsable JSON, which would
                        // surface as a refusal that reads like the model's mistake rather than the
                        // allowance's. The cut is named instead, and the owner is told what to do.
                        if (response.ToolCalls.Count > 0) {
                            await FailAsync(queued, stream, "reply_truncated",
                                "The reply reached the tokens this request left for it, cutting a tool call in half. "
                                + "Ask for a smaller change, or start a new conversation.",
                                null, step, runTimeout.Token);
                            return;
                        }
                        stream.Publish("run.warning", new { code = "length", message = "The response reached the tokens this request left for it." });
                    }

                    if (response.ToolCalls.Count == 0) {
                        // A message that stopped at the token limit is stored as truncated, so the reason
                        // survives the reload that the warning in the status line does not.
                        string status = response.FinishReason == "length" ? "truncated" : "complete";
                        await PersistAssistantAsync(queued, stream, step, response, status, includeToolCalls: false, runTimeout.Token);
                        partial = null;
                        await CompleteAsync(queued, stream, step, budgets, usageTotals, run.Model, systemPrompt,
                            calibration, budget, estimateError, lastMaxTokens, stepLimit, toolCallLimit);
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
                                queued, stream, step, RunBudget.ToolCallsPerStep, callsThisStep - 1, perStepLimit,
                                options.MaxToolCallsPerStep, runTimeout.Token);
                        }
                        if (toolCallCount >= toolCallLimit) {
                            toolCallLimit = await PauseAsync(
                                queued, stream, step, RunBudget.ToolCalls, toolCallCount, toolCallLimit,
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

                        // The row counts the calls the run has attempted, which is what the cap spends,
                        // and it moves as the card appears rather than when the step is recorded. The cost
                        // of the call is known before it runs, so it is published in the same snapshot.
                        budgets = budgets with { ToolCalls = toolCallCount + 1 };
                        if (call.Arguments.Length > budgets.LongestArguments) {
                            budgets = budgets with { LongestArguments = call.Arguments.Length };
                        }
                        PublishBudgets(stream, budgets, usageTotals, budget, stepLimit, toolCallLimit);

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
                                ResolveApprovalAsync(queued, stream, step, call, definition!, toolContext, runTimeout.Token));
                        } else {
                            result = await WhilePaused(() =>
                                ExecuteToolAsync(call, toolContext, stream, runTimeout.Token));
                        }

                        toolCallCount++;
                        await StoreAsync(store => store.RecordToolCallAsync(queued.RunId, runTimeout.Token));
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
                            PublishBudgets(stream, budgets, usageTotals, budget, stepLimit, toolCallLimit);
                        }
                        activeToolCallId = null;
                        activeToolName = null;
                    }
                }

                // The steps are spent. Waiting for the owner is not the provider going quiet, so the
                // run's silence clock stops while the question is on the page.
                stepLimit = await PauseAsync(
                    queued, stream, step - 1, RunBudget.Steps, step - 1, stepLimit, options.MaxSteps, runTimeout.Token);
                PublishBudgets(stream, budgets, usageTotals, budget, stepLimit, toolCallLimit);
            }
        } catch (ProviderException exception) {
            // The exception, not just its code: the inner exception is the socket, TLS or parse
            // failure that says which end of the connection was broken.
            logger.LogWarning(exception, "Assistant provider request for run {RunId} failed with {ErrorCode}", queued.RunId, exception.Code);
            await FailAsync(queued, stream, exception.Code, exception.Message, partial, step, CancellationToken.None);
        } catch (OperationCanceledException) {
            string status = hostStopping.IsCancellationRequested ? RunStatus.Interrupted
                : stream.StopToken.IsCancellationRequested ? RunStatus.Cancelled : RunStatus.Failed;
            string code = status switch {
                RunStatus.Interrupted => "restart",
                RunStatus.Cancelled => "stop",
                _ => "run_timeout",
            };
            string message = status switch {
                RunStatus.Interrupted => "The host stopped before the run completed.",
                RunStatus.Cancelled => "The run was stopped.",
                _ => "The run stopped responding for longer than its configured time limit.",
            };

            // A stop and a restart are the site working as told; a run that fell silent is the
            // provider, which we cannot fix and should still hear about.
            if (status == RunStatus.Interrupted) {
                logger.LogInformation("Assistant run {RunId} was interrupted by the host stopping", queued.RunId);
            } else if (status == RunStatus.Cancelled) {
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
                    new MessageInput("tool", ToolResults.Refusal(message, MessageText.Sanitize(activeToolName ?? "tool")).Content,
                        ToolCallId: activeToolCallId, ToolName: activeToolName),
                    CancellationToken.None));
            }
            await StoreAsync(store => store.FailPendingInterruptAsync(queued.RunId, code, CancellationToken.None));
            if (status == RunStatus.Cancelled) {
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
            views.Forget(view);
            registry.Complete(queued.RunId, queued.ConversationId);
            registry.RemoveExpired();
        }
    }

    // Recall is an enhancement, so nothing here can take the run down: a store that throws costs the
    // run its memory and a line in the transcript, and a lost use count is not worth a failed run.
    private async Task<string> RecallMemoryAsync(
        QueuedRun queued,
        RunStream stream,
        IReadOnlyList<Message> transcript,
        ContextBudget budget,
        CancellationToken cancellationToken) {
        Message? newest = transcript.LastOrDefault(message => message.Role == "user");
        string terms = newest is null ? string.Empty : MemoryQuery.Terms(newest.Content, options.MaxMemoryQueryTerms);

        try {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IMemoryStore memories = scope.ServiceProvider.GetRequiredService<IMemoryStore>();

            IReadOnlyList<MemoryCandidate> matched = terms.Length == 0
                ? []
                : await memories.RecallAsync(queued.Owner, terms, options.MaxMemorySearchResults, cancellationToken);
            IReadOnlyList<MemoryCandidate> used = await memories.StickyAsync(queued.Owner, queued.ConversationId, cancellationToken);
            IReadOnlyList<MemoryCandidate> pinned = await memories.PinnedAsync(queued.Owner, cancellationToken);

            // Recall is bounded by the memory block's share of the budget rather than a count: relevance
            // already ranks, and the budget is what decides how much of it the model may read.
            int memoryChars = (int)Math.Min(budget.MemoryCharsPerRun, int.MaxValue);
            MemorySelection selection = MemoryRecall.Select(pinned, used, matched, memoryChars);
            metrics.RecordMemoryRecall(selection.Memories.Count, selection.Characters);
            if (selection.IsEmpty) {
                return string.Empty;
            }

            logger.LogInformation("Assistant run {RunId} recalled {Memories} memories ({Chars} chars)",
                queued.RunId, selection.Memories.Count, selection.Characters);
            try {
                await memories.TouchAsync(
                    queued.Owner, [.. selection.Memories.Select(memory => memory.Id)], queued.ConversationId, cancellationToken);
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogWarning(exception, "Assistant run {RunId} could not record which memories it used", queued.RunId);
            }

            return MemoryRecall.Build(selection);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            logger.LogWarning(exception, "Assistant run {RunId} could not recall memories", queued.RunId);
            metrics.RecordMemoryRecall(0, 0);
            stream.Publish("run.warning", new {
                code = "memory_unavailable",
                message = "Memory could not be read, so this run continues without it.",
            });
            return string.Empty;
        }
    }

    private async Task<ToolExecutionResult> ResolveApprovalAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        PreparedToolCall call,
        ToolDefinition definition,
        ToolContext context,
        CancellationToken cancellationToken) {
        OwnerInterrupt? interrupt = await WaitAsync(
            queued, stream, step, OwnerInterruptKind.Approval, call.Id,
            new ApprovalRequest(call.Name, definition.Description, call.Arguments), cancellationToken);
        if (interrupt is null) {
            return ToolResults.Refusal("This tool call already has an interrupt record.", $"{call.DisplayName}: approval conflict");
        }

        bool approved = InterruptPayloads.Resolution(interrupt.ResolutionJson)?.Decision == "approve";
        return approved
            ? await ExecuteToolAsync(call, context, stream, cancellationToken)
            : ToolResults.Refusal("The owner denied this tool call.", $"{call.DisplayName}: denied");
    }

    /// <summary>Stops the run until its owner resolves the interrupt this opens.</summary>
    /// <returns>The resolved interrupt, or null when the run could not be stopped for the ask.</returns>
    private async Task<OwnerInterrupt?> WaitAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        string kind,
        string? toolCallId,
        object request,
        CancellationToken cancellationToken) {
        string requestJson = InterruptPayloads.Write(request);
        bool opened = await StoreAsync(store => store.OpenInterruptAsync(queued.RunId, kind, toolCallId, requestJson, cancellationToken));
        if (!opened) {
            return null;
        }

        stream.Publish("interrupt.required", new {
            step,
            runId = queued.RunId,
            kind,
            toolCallId,
            request,
        });
        logger.LogInformation("Assistant run {RunId} waits on its owner for a {Kind}", queued.RunId, kind);

        await interrupts.WaitAsync(queued.RunId, cancellationToken);

        OwnerInterrupt? interrupt = await StoreAsync(store => store.GetInterruptAsync(queued.RunId, cancellationToken));
        JsonElement? resolution = interrupt?.ResolutionJson is { } json
            ? JsonSerializer.Deserialize<JsonElement>(json)
            : null;
        stream.Publish("interrupt.resolved", new {
            step,
            runId = queued.RunId,
            kind,
            toolCallId,
            resolution,
        });
        return interrupt;
    }

    private async Task<ToolExecutionResult> ExecuteToolAsync(
        PreparedToolCall call,
        ToolContext context,
        RunStream stream,
        CancellationToken cancellationToken) {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ToolTimeoutSeconds));
        try {
            return await tools.ExecuteAsync(call.Name, call.Arguments, context, timeout.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ToolResults.Refusal("The tool exceeded its configured time limit.", $"{call.DisplayName}: timeout");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            logger.LogWarning(exception, "Assistant tool {ToolName} in run {RunId} failed", call.Name, stream.RunId);
            return ToolResults.Refusal("The tool could not complete this operation.", $"{call.DisplayName}: failed");
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
        long? messageId = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new MessageInput("assistant", response.Content, response.Reasoning,
                includeToolCalls ? SerializeToolCalls(response.ToolCalls) : null, Status: status),
            cancellationToken));
        if (messageId is { } stored) {
            stream.Publish("message.completed", new { step, messageId = stored, status, finishReason = response.FinishReason });
        }
    }

    private async Task PersistAssistantCallsAsync(
        QueuedRun queued,
        RunStream stream,
        int step,
        ChatResponse response,
        IReadOnlyList<PreparedToolCall> calls,
        CancellationToken cancellationToken) {
        long? messageId = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new MessageInput("assistant", response.Content, response.Reasoning, SerializePreparedToolCalls(calls)),
            cancellationToken));
        if (messageId is { } stored) {
            stream.Publish("message.completed", new { step, messageId = stored, status = "complete", finishReason = response.FinishReason });
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
        await StoreAsync(store => store.FailPendingInterruptAsync(queued.RunId, code, cancellationToken));
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, RunStatus.Failed, code, message, cancellationToken));
        stream.Publish("run.failed", new { runId = queued.RunId, code, message });
    }

    private async Task CompleteAsync(
        QueuedRun queued,
        RunStream stream,
        int steps,
        BudgetSnapshot budgets,
        RunUsage usage,
        string model,
        string systemPrompt,
        TokenCalibration? calibration,
        ContextBudget budget,
        long estimateError,
        int lastMaxTokens,
        int stepLimit,
        int toolCallLimit) {
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, RunStatus.Completed));
        // The last live figure is the request the next step would send, built from the transcript as
        // stored. The reload builds the same plan the same way, so it cannot correct what a reader
        // just watched. `Publish` throws once the stream is complete, so nothing may follow
        // `run.completed`.
        IReadOnlyList<Message> transcript = await StoreAsync(
            store => store.GetMessagesAsync(queued.ConversationId, queued.Owner, CancellationToken.None));
        List<ChatMessage> messages = [new ChatMessage("system", systemPrompt)];
        messages.AddRange(transcript.Select(message => message.ToChatMessage()));
        ChatRequest request = new(
            model,
            messages,
            PageToolExecutor.Definitions,
            options.Temperature,
            budget.MinimumOutputTokens,
            options.ReasoningField);
        ContextPlan plan = ContextPlanner.Build(request, budget, TokenEstimator.For(calibration));
        PublishBudgets(
            stream,
            BudgetSnapshot.Measure(
                plan, messages, steps, budgets.ToolCalls, usage.LastCompletionTokens, lastMaxTokens, budget, estimateError),
            usage,
            budget,
            stepLimit,
            toolCallLimit);
        stream.Publish("run.completed", new {
            runId = queued.RunId,
            steps,
            usage,
        });
    }

    // The keys are the row names the page's markup carries, so the browser needs no translation and
    // `message` is absent: the textarea is that row's only source.
    private static void PublishBudgets(
        RunStream stream,
        BudgetSnapshot budgets,
        RunUsage usage,
        ContextBudget budget,
        int stepLimit,
        int toolCallLimit) => stream.Publish("budget.updated", new {
            budgets = new Dictionary<string, long>(StringComparer.Ordinal) {
                ["context"] = budgets.ContextTokens,
                ["reply"] = budgets.MaxTokens,
                ["window"] = budgets.WindowUsed,
                ["estimate"] = budgets.EstimateErrorMagnitude,
                ["steps"] = budgets.Steps,
                ["tool-calls"] = budgets.ToolCalls,
                ["tool-arguments"] = budgets.LongestArguments,
                ["tool-result"] = budgets.LongestResult,
                ["output"] = budgets.LastCompletionTokens,
            },
            limits = new Dictionary<string, long>(StringComparer.Ordinal) {
                ["context"] = budget.PromptBudget,
                ["window"] = budgets.Window,
                ["estimate"] = budget.EstimateMargin,
                ["output"] = budgets.LastMaxTokens,
                ["steps"] = stepLimit,
                ["tool-calls"] = toolCallLimit,
            },
            estimate = new {
                direction = budgets.EstimateDirection,
            },
            usage = new {
                prompt = usage.PromptTokens,
                cached = usage.CachedTokens,
                completion = usage.CompletionTokens,
                reasoning = usage.ReasoningTokens,
                total = usage.TotalTokens,
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

    private sealed record PreparedToolCall(string Id, string Name, string Arguments) {

        public string DisplayName => MessageText.Sanitize(Name);
    }
}
