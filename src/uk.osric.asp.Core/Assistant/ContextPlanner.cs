// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The request to send and what it took to fit it into the budget.</summary>
/// <param name="Request">The request the provider is sent, with its <c>max_tokens</c> resolved.</param>
/// <param name="MaskedToolResults">How many old tool results were replaced with placeholders.</param>
/// <param name="MaskedArguments">How many long argument strings were elided.</param>
/// <param name="DroppedMessages">How many transcript messages were dropped with their turns.</param>
/// <param name="OriginalTokens">The request's estimated size before anything was masked or dropped.</param>
/// <param name="EstimatedTokens">The estimated size of <paramref name="Request"/> as it will be sent.</param>
/// <param name="RequestBytes">The exact UTF-8 bytes of <paramref name="Request"/>, for calibration.</param>
/// <param name="Error">Why no request can fit, when even the smallest one is too large.</param>
public sealed record ContextPlan(
    ChatRequest Request,
    int MaskedToolResults,
    int MaskedArguments,
    int DroppedMessages,
    long OriginalTokens,
    long EstimatedTokens,
    long RequestBytes,
    string? Error) {

    /// <summary>Whether any old tool result or argument payload was elided.</summary>
    public bool IsMasked => MaskedToolResults > 0 || MaskedArguments > 0;

    /// <summary>Whether any whole turn was dropped.</summary>
    public bool IsTrimmed => DroppedMessages > 0;

    /// <summary>The <c>max_tokens</c> the planned request is sent with.</summary>
    public int MaxTokens => Request.MaxTokens;
}

/// <summary>Fits a request into a token budget: mask old payloads first, then drop whole turns.</summary>
/// <remarks>
/// Masking runs first because it loses less: it keeps every user and assistant word, the tool names and
/// the short arguments, and gives up only old payloads. The trimmer is the fallback for a conversation
/// whose old reasoning is the bulk. A request that is already inside the budget is returned untouched,
/// so the common step keeps its prefix byte-identical and the provider's prompt cache intact.
/// <para>
/// The size is the provider's own count of the last request plus an estimate of what has been appended
/// since; see <see cref="TokenEstimator"/>. Each message is measured once, and masking a message or
/// dropping a turn is arithmetic over those figures. The reply's allowance follows the estimate, so
/// what the prompt leaves and what the reply may use still add up to the window.
/// </para>
/// </remarks>
public static class ContextPlanner {

    /// <summary>Builds the request to send, fitting it into <paramref name="budget"/>.</summary>
    /// <param name="request">The full request, with the system prompt, tool definitions and transcript.</param>
    /// <param name="budget">The model's window and the reply's floor.</param>
    /// <param name="estimator">The estimator the budget is measured with.</param>
    /// <returns>The plan, with the request to send, what changed, and any error.</returns>
    public static ContextPlan Build(ChatRequest request, ContextBudget budget, TokenEstimator estimator) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(estimator);
        if (request.Messages is null) {
            throw new ArgumentNullException(nameof(request), "The request has no messages.");
        }

        long maximumTokens = budget.PromptBudget;
        PromptSizes sizes = PromptSizes.Measure(request);
        long originalTokens = estimator.Estimate(sizes.Total);
        if (originalTokens <= maximumTokens) {
            return Finish(request, sizes, budget, estimator, 0, 0, 0, originalTokens, null);
        }

        List<ChatMessage> messages = [.. request.Messages];
        long[] messageBytes = [.. sizes.Sizes];
        int maskedResults = 0;
        int maskedArguments = 0;
        foreach (int index in ToolResultMasker.MaskableMessages(messages)) {
            MaskOutcome outcome = messages[index].Mask();
            if (!outcome.Changed) {
                continue;
            }

            messages[index] = outcome.Message;
            messageBytes[index] = ChatRequestJson.MeasureMessage(outcome.Message);
            maskedResults += outcome.MaskedResults;
            maskedArguments += outcome.MaskedArguments;
            long candidateBytes = sizes.Body(Sum(messageBytes), messageBytes.Length);
            if (estimator.Estimate(candidateBytes) <= maximumTokens) {
                return Finish(
                    request with { Messages = messages }, sizes, budget, estimator,
                    maskedResults, maskedArguments, 0, originalTokens, null);
            }
        }

        ContextTrimResult trimmed = ContextTrimmer.Trim(request with { Messages = messages }, maximumTokens, estimator);
        ChatRequest final = WithMaxTokens(trimmed.Request, trimmed.EstimatedTokens, budget);
        long bytes = ChatRequestJson.MeasureBytes(final);
        long estimate = estimator.Estimate(bytes);
        if (estimate != trimmed.EstimatedTokens) {
            final = WithMaxTokens(trimmed.Request, estimate, budget);
            bytes = ChatRequestJson.MeasureBytes(final);
        }
        return new ContextPlan(
            final, maskedResults, maskedArguments, trimmed.DroppedMessages, originalTokens, estimate, bytes, trimmed.Error);
    }

    private static ContextPlan Finish(
        ChatRequest request,
        PromptSizes sizes,
        ContextBudget budget,
        TokenEstimator estimator,
        int maskedResults,
        int maskedArguments,
        int dropped,
        long originalTokens,
        string? error) {
        long estimate = estimator.Estimate(sizes.Of(request.Messages));
        ChatRequest final = WithMaxTokens(request, estimate, budget);

        // The allowance is part of the body, so setting it can move the measurement by a digit or two.
        // One refinement is enough: the estimate lands on the same figure it was given.
        long bytes = ChatRequestJson.MeasureBytes(final);
        long refined = estimator.Estimate(bytes);
        if (refined != estimate) {
            final = WithMaxTokens(request, refined, budget);
            bytes = ChatRequestJson.MeasureBytes(final);
            estimate = refined;
        }

        return new ContextPlan(final, maskedResults, maskedArguments, dropped, originalTokens, estimate, bytes, error);
    }

    private static ChatRequest WithMaxTokens(ChatRequest request, long estimatedPrompt, ContextBudget budget) {
        int allowance = budget.MaxTokens(estimatedPrompt);
        return request.MaxTokens == allowance ? request : request with { MaxTokens = allowance };
    }

    private static long Sum(IReadOnlyList<long> sizes) {
        long sum = 0;
        foreach (long size in sizes) {
            sum += size;
        }
        return sum;
    }
}
