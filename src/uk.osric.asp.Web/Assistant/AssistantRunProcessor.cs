// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Storage;

/// <summary>Runs one queued assistant run to completion, streaming its events and executing its tools.</summary>
internal sealed class AssistantRunProcessor(
    IServiceScopeFactory scopeFactory,
    IChatCompletionClient client,
    AssistantPageToolExecutor tools,
    AssistantRunRegistry registry,
    AssistantToolApprovalCoordinator approvals,
    AssistantMetrics metrics,
    AssistantOptions options,
    ILogger<AssistantRunProcessor> logger) {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(AssistantQueuedRun queued, CancellationToken hostStopping) {
        long runStart = Stopwatch.GetTimestamp();
        AssistantRunStream? stream = registry.Find(queued.RunId);
        if (stream is null) {
            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "interrupted", "missing_journal", "The run could not be resumed."));
            return;
        }

        using CancellationTokenSource runTimeout = CancellationTokenSource.CreateLinkedTokenSource(hostStopping, stream.StopToken);
        runTimeout.CancelAfter(TimeSpan.FromSeconds(options.RunTimeoutSeconds));
        AssistantChatStreamAccumulator? partial = null;
        int step = 0;
        string? activeToolCallId = null;
        string? activeToolName = null;

        try {
            AssistantRun? run = await StoreAsync(store => store.GetActiveRunAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
            if (run is null || run.Id != queued.RunId) {
                await FailAsync(queued, stream, "run_missing", "The run is no longer active.", partial, step, CancellationToken.None);
                return;
            }

            if (stream.StopToken.IsCancellationRequested) {
                throw new OperationCanceledException(stream.StopToken);
            }

            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "running", cancellationToken: runTimeout.Token));
            stream.Publish("run.started", new { runId = queued.RunId, model = run.Model, promptVersion = run.PromptVersion });
            logger.LogInformation("Assistant run {RunId} in conversation {ConversationId} started with model {Model}",
                queued.RunId, queued.ConversationId, run.Model);

            int toolCallCount = 0;
            for (step = 1; step <= options.MaxSteps; step++) {
                IReadOnlyList<AssistantMessage> transcript = await StoreAsync(
                    store => store.GetMessagesAsync(queued.ConversationId, queued.Owner, runTimeout.Token));
                List<AssistantChatMessage> messages = [new AssistantChatMessage("system", PageAuthorPrompt.Build())];
                messages.AddRange(transcript.Select(ToChatMessage));
                AssistantContextTrimResult trimmed = AssistantContextTrimmer.Trim(messages, options.MaxContextChars);
                if (trimmed.Error is not null) {
                    await FailAsync(queued, stream, "context_too_long", trimmed.Error, partial, step, runTimeout.Token);
                    return;
                }
                if (trimmed.WasTrimmed) {
                    stream.Publish("run.warning", new {
                        code = "context_trimmed",
                        message = trimmed.TrimmingNote,
                    });
                }

                stream.Publish("message.started", new { step });
                partial = new AssistantChatStreamAccumulator();
                AssistantChatRequest request = new(
                    run.Model,
                    trimmed.Messages,
                    AssistantPageToolExecutor.Definitions,
                    options.Temperature,
                    options.MaxOutputTokens,
                    options.ReasoningField,
                    options.IncludeUsage);

                long providerStart = Stopwatch.GetTimestamp();
                bool firstTokenRecorded = false;
                string providerOutcome = "ok";
                try {
                    await foreach (AssistantChatStreamEvent @event in client.StreamAsync(request, runTimeout.Token)) {
                        partial.Add(@event);
                        if (!firstTokenRecorded && @event is AssistantContentDelta or AssistantReasoningDelta or AssistantToolCallDelta) {
                            metrics.RecordProviderFirstToken(run.Model, Stopwatch.GetElapsedTime(providerStart));
                            firstTokenRecorded = true;
                        }
                        switch (@event) {
                            case AssistantContentDelta content:
                                stream.Publish("content.delta", new { step, text = content.Text });
                                break;
                            case AssistantReasoningDelta reasoning:
                                stream.Publish("reasoning.delta", new { step, text = reasoning.Text });
                                break;
                        }
                    }
                } catch (AssistantProviderException exception) {
                    providerOutcome = exception.Code;
                    throw;
                } catch (OperationCanceledException) {
                    providerOutcome = "cancelled";
                    throw;
                } catch (HttpRequestException exception) {
                    providerOutcome = "provider_unreachable";
                    throw new AssistantProviderException(providerOutcome, "The configured provider could not be reached.", exception);
                } catch (IOException exception) {
                    providerOutcome = "provider_unreachable";
                    throw new AssistantProviderException(providerOutcome, "The configured provider could not be reached.", exception);
                } finally {
                    metrics.RecordProviderRequest(run.Model, providerOutcome, Stopwatch.GetElapsedTime(providerStart));
                }

                AssistantChatResponse response = partial.Complete();
                if (response.Usage is { } usage) {
                    metrics.RecordUsage(usage);
                }
                await StoreAsync(store => store.RecordStepAsync(queued.RunId, response.ToolCalls.Count, response.Usage, runTimeout.Token));
                if (response.FinishReason == "content_filter") {
                    await PersistAssistantAsync(queued, stream, step, response, "failed", includeToolCalls: false, CancellationToken.None);
                    await FailAsync(queued, stream, "content_filter", "The assistant refused to continue.", null, step, runTimeout.Token);
                    return;
                }
                if (!response.HasContent) {
                    await FailAsync(queued, stream, "empty_response", "The provider returned an empty response.", null, step, runTimeout.Token);
                    return;
                }

                if (response.FinishReason == "length") {
                    stream.Publish("run.warning", new { code = "length", message = "The response reached the provider token limit." });
                }

                if (response.ToolCalls.Count == 0) {
                    await PersistAssistantAsync(queued, stream, step, response, "complete", includeToolCalls: false, runTimeout.Token);
                    partial = null;
                    await CompleteAsync(queued, stream, step, response);
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
                    AssistantToolExecutionResult result;
                    AssistantToolDefinition? definition = AssistantPageToolExecutor.Definitions.SingleOrDefault(tool => tool.Name == call.Name);
                    bool requiresApproval = run.ApprovalMode == AssistantApprovalMode.RequireApproval.ToString()
                        && definition?.NeedsApproval == true;
                    stream.Publish("tool.started", new {
                        step,
                        toolCallId = call.Id,
                        name = call.Name,
                        arguments = call.Arguments,
                        summary = $"Using {call.DisplayName}",
                    });

                    long toolStart = Stopwatch.GetTimestamp();
                    if (callsThisStep > options.MaxToolCallsPerStep) {
                        result = ToolFailure("The provider exceeded the per-step tool-call limit.", $"{call.DisplayName}: per-step tool-call limit");
                    } else if (toolCallCount >= options.MaxToolCalls) {
                        result = ToolFailure("The run reached its tool-call limit.", $"{call.DisplayName}: tool-call limit");
                    } else if (requiresApproval) {
                        result = await ResolveApprovalAsync(queued, stream, step, call, definition!, runTimeout.Token);
                    } else {
                        result = await ExecuteToolAsync(call, stream, step, runTimeout.Token);
                    }

                    toolCallCount++;
                    metrics.RecordTool(definition, result.Success, Stopwatch.GetElapsedTime(toolStart));
                    await StoreAsync(store => store.AppendMessageAsync(
                        queued.ConversationId,
                        queued.RunId,
                        new AssistantMessageInput("tool", result.Content, ToolCallId: call.Id, ToolName: call.Name),
                        runTimeout.Token));
                    stream.Publish("tool.finished", new {
                        step,
                        toolCallId = call.Id,
                        name = call.Name,
                        arguments = call.Arguments,
                        ok = result.Success,
                        summary = result.Summary,
                        content = result.Content,
                        durationMs = (long)Stopwatch.GetElapsedTime(toolStart).TotalMilliseconds,
                    });
                    activeToolCallId = null;
                    activeToolName = null;
                }
            }

            await FailAsync(queued, stream, "max_steps", "The assistant used too many steps.", null, step - 1, runTimeout.Token);
        } catch (AssistantProviderException exception) {
            logger.LogWarning("Assistant provider request for run {RunId} failed with {ErrorCode}", queued.RunId, exception.Code);
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
                _ => "The run exceeded its configured time limit.",
            };
            await PersistPartialAsync(queued, stream, step, partial, status, CancellationToken.None);
            if (activeToolCallId is not null) {
                await StoreAsync(store => store.AppendMessageAsync(
                    queued.ConversationId,
                    queued.RunId,
                    new AssistantMessageInput("tool", ToolFailure(message, MessageText.Sanitize(activeToolName ?? "tool")).Content,
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
                AssistantRun? finalRun = await StoreAsync(store => store.GetRunAsync(
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

    private async Task<AssistantToolExecutionResult> ResolveApprovalAsync(
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        int step,
        PreparedToolCall call,
        AssistantToolDefinition definition,
        CancellationToken cancellationToken) {
        using CancellationTokenSource waiterCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<AssistantApprovalResolutionResult> wait = approvals.WaitAsync(queued.RunId, call.Id, waiterCancellation.Token);
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

        try {
            AssistantApprovalResolutionResult decision = await wait;
            await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "running", cancellationToken: cancellationToken));
            stream.Publish("approval.resolved", new { step, toolCallId = call.Id, decision = decision == AssistantApprovalResolutionResult.Approved ? "approved" : "denied" });
            return decision == AssistantApprovalResolutionResult.Approved
                ? await ExecuteToolAsync(call, stream, step, cancellationToken)
                : ToolFailure("The owner denied this tool call.", $"{call.DisplayName}: denied");
        } catch (OperationCanceledException) {
            throw;
        }
    }

    private async Task<AssistantToolExecutionResult> ExecuteToolAsync(
        PreparedToolCall call,
        AssistantRunStream stream,
        int step,
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
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        int step,
        AssistantChatResponse response,
        string status,
        bool includeToolCalls,
        CancellationToken cancellationToken) {
        long? sequence = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new AssistantMessageInput("assistant", response.Content, response.Reasoning,
                includeToolCalls ? SerializeToolCalls(response.ToolCalls) : null, Status: status),
            cancellationToken));
        if (sequence is { } messageSequence) {
            stream.Publish("message.completed", new { step, messageId = messageSequence, status, finishReason = response.FinishReason });
        }
    }

    private async Task PersistAssistantCallsAsync(
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        int step,
        AssistantChatResponse response,
        IReadOnlyList<PreparedToolCall> calls,
        CancellationToken cancellationToken) {
        long? sequence = await StoreAsync(store => store.AppendMessageAsync(
            queued.ConversationId,
            queued.RunId,
            new AssistantMessageInput("assistant", response.Content, response.Reasoning, SerializePreparedToolCalls(calls)),
            cancellationToken));
        if (sequence is { } messageSequence) {
            stream.Publish("message.completed", new { step, messageId = messageSequence, status = "complete", finishReason = response.FinishReason });
        }
    }

    private async Task PersistPartialAsync(
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        int step,
        AssistantChatStreamAccumulator? accumulator,
        string status,
        CancellationToken cancellationToken) {
        if (accumulator is null) {
            return;
        }
        AssistantChatResponse response = accumulator.Complete();
        if (response.Content.Length == 0 && response.Reasoning.Length == 0) {
            return;
        }
        await PersistAssistantAsync(queued, stream, step, response, status, includeToolCalls: false, cancellationToken);
    }

    private async Task FailAsync(
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        string code,
        string message,
        AssistantChatStreamAccumulator? partial,
        int step,
        CancellationToken cancellationToken) {
        await PersistPartialAsync(queued, stream, step, partial, "failed", cancellationToken);
        await StoreAsync(store => store.FailPendingApprovalsAsync(queued.RunId, code, cancellationToken));
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "failed", code, message, cancellationToken));
        stream.Publish("run.failed", new { runId = queued.RunId, code, message });
    }

    private async Task CompleteAsync(
        AssistantQueuedRun queued,
        AssistantRunStream stream,
        int steps,
        AssistantChatResponse response) {
        await StoreAsync(store => store.SetRunStatusAsync(queued.RunId, "completed"));
        stream.Publish("run.completed", new {
            runId = queued.RunId,
            steps,
            usage = response.Usage,
        });
    }

    private async Task<T> StoreAsync<T>(Func<IAssistantConversationStore, Task<T>> action) {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAssistantConversationStore>());
    }

    private static AssistantChatMessage ToChatMessage(AssistantMessage message) => new(
        message.Role,
        message.Content,
        message.ToolCallsJson,
        message.ToolCallId,
        message.ToolName);

    private static IReadOnlyList<PreparedToolCall> PrepareToolCalls(IReadOnlyList<AssistantToolCall> calls, int step) => [.. calls.Select(call => new PreparedToolCall(
        call.Id ?? $"missing-{step}-{call.Index}",
        call.Name ?? string.Empty,
        call.Arguments))];

    private static string SerializeToolCalls(IReadOnlyList<AssistantToolCall> calls) =>
        SerializePreparedToolCalls(PrepareToolCalls(calls, 1));

    private static string SerializePreparedToolCalls(IReadOnlyList<PreparedToolCall> calls) => JsonSerializer.Serialize(
        calls.Select(call => new {
            id = call.Id,
            type = "function",
            function = new { name = call.Name, arguments = call.Arguments },
        }), s_jsonOptions);

    private static AssistantToolExecutionResult ToolFailure(string error, string summary) => new(
        false,
        JsonSerializer.Serialize(new { ok = false, data = (object?)null, error }, s_jsonOptions),
        summary);

    private sealed record PreparedToolCall(string Id, string Name, string Arguments) {

        public string DisplayName => MessageText.Sanitize(Name);
    }
}
