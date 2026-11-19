// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>The outcome of offering one message to the masker.</summary>
/// <param name="Message">The message to send: the original when nothing was elided.</param>
/// <param name="MaskedResults">1 when a tool result was replaced with a placeholder.</param>
/// <param name="MaskedArguments">How many long argument strings were elided.</param>
internal sealed record MaskOutcome(ChatMessage Message, int MaskedResults, int MaskedArguments) {

    /// <summary>Whether anything in the message changed.</summary>
    public bool Changed => MaskedResults > 0 || MaskedArguments > 0;
}

/// <summary>Replaces old tool results and old argument payloads in a request with placeholders.</summary>
/// <remarks>
/// This is a derived, per-request rule: the stored transcript is never rewritten. Nothing is exempt:
/// masking walks oldest first, so the newest payloads are the last to be given up, and it stops the
/// moment the request fits. A count would add an exemption that can bite back — eight protected
/// results are more tokens than the whole budget — and the order already says what matters
/// (<c>assistant-old-tool-results-are-masked</c>).
/// </remarks>
internal static class ToolResultMasker {

    /// <summary>The longest argument string a request may carry once a call has been masked.</summary>
    public const int MaximumArgumentLength = 1_024;

    /// <summary>How far into a nested argument object the masker walks.</summary>
    public const int MaximumArgumentDepth = 8;

    private const string ResultPlaceholder = "[tool result elided: ";

    /// <summary>Finds the messages the masker may shorten, oldest first.</summary>
    /// <param name="messages">The request's messages, in provider order.</param>
    /// <returns>The indices that masking would change. The order is the stored sequence order.</returns>
    public static IReadOnlyList<int> MaskableMessages(IReadOnlyList<ChatMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        List<int> indices = [];
        for (int index = 0; index < messages.Count; index++) {
            if (messages[index].Mask().Changed) {
                indices.Add(index);
            }
        }
        return indices;
    }

    /// <summary>Masks one message's payloads.</summary>
    /// <param name="message">The message to offer to the masker.</param>
    /// <returns>The message to send and what was elided.</returns>
    public static MaskOutcome Mask(this ChatMessage message) {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Role == "tool") {
            return MaskToolResult(message);
        }
        if (message.Role == "assistant" && message.ToolCallsJson is not null) {
            return MaskToolCallArguments(message);
        }
        return new MaskOutcome(message, 0, 0);
    }

    private static MaskOutcome MaskToolResult(ChatMessage message) {
        // A placeholder is already the masked form: masking it again would only rewrite its own
        // character count, and the plan must give the same bytes twice.
        if (message.Content.StartsWith(ResultPlaceholder, StringComparison.Ordinal)) {
            return new MaskOutcome(message, 0, 0);
        }

        string placeholder = $"{ResultPlaceholder}{message.Content.Length} chars]";
        return placeholder.Length < message.Content.Length
            ? new MaskOutcome(message with { Content = placeholder }, 1, 0)
            : new MaskOutcome(message, 0, 0);
    }

    private static MaskOutcome MaskToolCallArguments(ChatMessage message) {
        JsonNode? root;
        try {
            root = JsonNode.Parse(message.ToolCallsJson!);
        } catch (JsonException) {
            return new MaskOutcome(message, 0, 0);
        }

        if (root is not JsonArray calls) {
            return new MaskOutcome(message, 0, 0);
        }

        int masked = 0;
        foreach (JsonNode? call in calls) {
            if (call is not JsonObject callObject
                || callObject["function"] is not JsonObject function
                || function["arguments"] is not JsonValue argumentsValue
                || !argumentsValue.TryGetValue(out string? arguments)) {
                continue;
            }

            JsonNode? parsed;
            try {
                parsed = JsonNode.Parse(arguments);
            } catch (JsonException) {
                // A corrupt call is left exactly as it is: the executor would have refused it, and the
                // planner must not turn it into a new failure.
                continue;
            }
            if (parsed is null) {
                continue;
            }

            int elided = ElideLongStrings(parsed, depth: 0);
            if (elided > 0) {
                function["arguments"] = parsed.ToJsonString();
                masked += elided;
            }
        }

        return masked == 0
            ? new MaskOutcome(message, 0, 0)
            : new MaskOutcome(message with { ToolCallsJson = calls.ToJsonString() }, 0, masked);
    }

    private static int ElideLongStrings(JsonNode node, int depth) {
        if (depth >= MaximumArgumentDepth) {
            return 0;
        }

        int masked = 0;
        if (node is JsonObject item) {
            foreach (string key in item.Select(pair => pair.Key).ToList()) {
                masked += ElideValue(item, key, depth);
            }
        } else if (node is JsonArray array) {
            for (int index = 0; index < array.Count; index++) {
                masked += ElideValue(array, index, depth);
            }
        }
        return masked;
    }

    private static int ElideValue(JsonObject item, string key, int depth) =>
        item[key] is { } value ? ElideValue(value, depth, placeholder => item[key] = placeholder) : 0;

    private static int ElideValue(JsonArray array, int index, int depth) =>
        array[index] is { } value ? ElideValue(value, depth, placeholder => array[index] = placeholder) : 0;

    private static int ElideValue(JsonNode value, int depth, Action<JsonNode> replace) {
        if (value is JsonObject or JsonArray) {
            return ElideLongStrings(value, depth + 1);
        }

        if (value is not JsonValue scalar || !scalar.TryGetValue(out string? text)) {
            return 0;
        }

        string placeholder = $"[elided {text.Length} chars]";
        if (text.Length <= MaximumArgumentLength || placeholder.Length >= text.Length) {
            return 0;
        }

        replace(JsonValue.Create(placeholder));
        return 1;
    }
}
