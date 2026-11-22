// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Text;

/// <summary>The one serializer for the provider's chat-completions body, and the one measure of it.</summary>
/// <remarks>
/// The client sends exactly what <see cref="Serialize"/> returns, and every budget figure counts exactly
/// that body, so a field added here moves the request and the meter together. It is pure: it parses the
/// stored tool-call and parameter JSON, builds the body, and serializes it, with no I/O and no state.
/// </remarks>
public static class ChatRequestJson {

    /// <summary>Builds the JSON body the provider receives.</summary>
    /// <param name="request">The request to serialize.</param>
    /// <returns>The body, as UTF-8 text.</returns>
    public static string Serialize(ChatRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        List<Dictionary<string, object?>> messages = [.. request.Messages.Select(Message)];

        object[] tools = [.. request.Tools.Select(tool => {
            using JsonDocument parameters = JsonDocument.Parse(tool.ParametersJson);
            return (object)new {
                type = "function",
                function = new {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = parameters.RootElement.Clone(),
                },
            };
        })];
        Dictionary<string, object?> body = new(StringComparer.Ordinal) {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["tools"] = tools,
            ["tool_choice"] = "auto",
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            // The provider's usage chunk is what anchors every estimate, so it is asked for on every
            // request rather than left to a setting that could silently switch the budget off.
            ["stream_options"] = new { include_usage = true },
        };

        return JsonSerializer.Serialize(body, JsonSerialization.Options);
    }

    /// <summary>Measures one message as the body carries it, in UTF-8 bytes.</summary>
    /// <param name="message">The message to measure.</param>
    /// <returns>The byte count of the message's own JSON object.</returns>
    /// <remarks>
    /// The planner sizes each message once and then adds and subtracts those figures as it masks and
    /// drops, so this is the one place a message's cost is counted.
    /// </remarks>
    internal static long MeasureMessage(ChatMessage message) {
        ArgumentNullException.ThrowIfNull(message);
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(Message(message), JsonSerialization.Options));
    }

    private static Dictionary<string, object?> Message(ChatMessage source) {
        Dictionary<string, object?> message = new(StringComparer.Ordinal) {
            ["role"] = source.Role,
            ["content"] = source.Content,
        };
        if (source.ToolCallId is not null) {
            message["tool_call_id"] = source.ToolCallId;
        }
        if (source.ToolName is not null) {
            message["name"] = source.ToolName;
        }
        if (source.ToolCallsJson is not null) {
            using JsonDocument calls = JsonDocument.Parse(source.ToolCallsJson);
            message["tool_calls"] = calls.RootElement.Clone();
        }
        return message;
    }

    /// <summary>Measures the body the provider receives, in UTF-8 bytes.</summary>
    /// <param name="request">The request to measure.</param>
    /// <returns>The byte count of <see cref="Serialize"/>.</returns>
    /// <remarks>
    /// This is the single place a request's size is counted: the planner, the trimmer and the page all
    /// ask here rather than building their own figure. A token budget estimates tokens from this byte
    /// count with <see cref="TokenEstimator"/>, calibrated by the provider's last report, and leaves
    /// the rules alone.
    /// </remarks>
    public static long MeasureBytes(ChatRequest request) => Encoding.UTF8.GetByteCount(Serialize(request));
}
