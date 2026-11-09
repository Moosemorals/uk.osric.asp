// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text;
using System.Text.Json;

/// <summary>The one serializer for the provider's chat-completions body, and the one measure of it.</summary>
/// <remarks>
/// The client sends exactly what <see cref="Serialize"/> returns, and every budget figure counts exactly
/// that body, so a field added here moves the request and the meter together. It is pure: it parses the
/// stored tool-call and parameter JSON, builds the body, and serializes it, with no I/O and no state.
/// </remarks>
public static class ChatRequestJson {

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Builds the JSON body the provider receives.</summary>
    /// <param name="request">The request to serialize.</param>
    /// <returns>The body, as UTF-8 text.</returns>
    public static string Serialize(ChatRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        List<Dictionary<string, object?>> messages = [];
        foreach (ChatMessage source in request.Messages) {
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
            messages.Add(message);
        }

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
            ["max_tokens"] = request.MaxOutputTokens,
        };
        if (request.IncludeUsage) {
            body["stream_options"] = new { include_usage = true };
        }

        return JsonSerializer.Serialize(body, s_jsonOptions);
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
