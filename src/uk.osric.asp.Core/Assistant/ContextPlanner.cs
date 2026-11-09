// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The request to send and what it took to fit it into the budget.</summary>
/// <param name="Request">The request the provider is sent.</param>
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

    /// <summary>Whether any old observation or argument payload was elided.</summary>
    public bool IsMasked => MaskedToolResults > 0 || MaskedArguments > 0;

    /// <summary>Whether any whole turn was dropped.</summary>
    public bool IsTrimmed => DroppedMessages > 0;
}

/// <summary>Fits a request into a token budget: mask old payloads first, then drop whole turns.</summary>
/// <remarks>
/// Masking runs first because it loses less: it keeps every user and assistant word, the tool names and
/// the short arguments, and gives up only old payloads. The trimmer is the fallback for a conversation
/// whose old reasoning is the bulk. A request that is already inside the budget is returned untouched,
/// so the common step keeps its prefix byte-identical and the provider's prompt cache intact.
/// <para>
/// The size is estimated from the exact serialized body and the provider's last report; see
/// <see cref="TokenEstimator"/>. The masking and trimming rules are the same whatever the unit is.
/// </para>
/// </remarks>
public static class ContextPlanner {

    /// <summary>Builds the request to send, fitting it into <paramref name="maximumTokens"/>.</summary>
    /// <param name="request">The full request, with the system prompt, tool definitions and transcript.</param>
    /// <param name="maximumTokens">The largest estimated prompt the provider may be sent.</param>
    /// <param name="estimator">The estimator the budget is measured with.</param>
    /// <returns>The plan, with the request to send, what changed, and any error.</returns>
    public static ContextPlan Build(ChatRequest request, long maximumTokens, TokenEstimator estimator) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTokens);
        if (request.Messages is null) {
            throw new ArgumentNullException(nameof(request), "The request has no messages.");
        }

        long originalBytes = ChatRequestJson.MeasureBytes(request);
        long originalTokens = estimator.Estimate(originalBytes);
        if (originalTokens <= maximumTokens) {
            return new ContextPlan(request, 0, 0, 0, originalTokens, originalTokens, originalBytes, null);
        }

        List<ChatMessage> messages = [.. request.Messages];
        IReadOnlyList<int> maskable = ObservationMasker.MaskableMessages(messages);
        int maskedResults = 0;
        int maskedArguments = 0;
        foreach (int index in maskable) {
            MaskOutcome outcome = ObservationMasker.MaskMessage(messages[index]);
            if (!outcome.Changed) {
                continue;
            }

            messages[index] = outcome.Message;
            maskedResults += outcome.MaskedResults;
            maskedArguments += outcome.MaskedArguments;
            ChatRequest candidate = request with { Messages = messages };
            long candidateBytes = ChatRequestJson.MeasureBytes(candidate);
            long candidateTokens = estimator.Estimate(candidateBytes);
            if (candidateTokens <= maximumTokens) {
                return new ContextPlan(
                    candidate, maskedResults, maskedArguments, 0, originalTokens, candidateTokens, candidateBytes, null);
            }
        }

        ChatRequest masked = request with { Messages = messages };
        ContextTrimResult trimmed = ContextTrimmer.Trim(masked, maximumTokens, estimator);
        return new ContextPlan(
            trimmed.Request,
            maskedResults,
            maskedArguments,
            trimmed.DroppedMessages,
            originalTokens,
            trimmed.EstimatedTokens,
            trimmed.RequestBytes,
            trimmed.Error);
    }
}
