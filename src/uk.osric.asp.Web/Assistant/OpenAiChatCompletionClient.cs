// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;

/// <summary>Streams completions from an OpenAI-compatible chat-completions endpoint.</summary>
internal sealed class OpenAiChatCompletionClient(HttpClient client, AssistantOptions options) : IChatCompletionClient {

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        // A step that keeps thinking is healthy however long it takes: the deadline is silence, not
        // duration. It is re-armed for every event the provider sends, so only a stalled stream ends.
        using CancellationTokenSource silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan allowed = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        silence.CancelAfter(allowed);

        HttpResponseMessage response;
        Stream body;
        try {
            response = await SendWithRetriesAsync(request, silence.Token);
            if (!response.IsSuccessStatusCode) {
                response.Dispose();
                throw ProviderError(response.StatusCode);
            }
            body = await response.Content.ReadAsStreamAsync(silence.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw ProviderStopped();
        }

        using (response) {
            await using (body) {
                await using IAsyncEnumerator<SseItem<string>> items = SseParser.Create(body)
                    .EnumerateAsync(silence.Token)
                    .GetAsyncEnumerator(silence.Token);
                while (true) {
                    bool moved;
                    try {
                        moved = await items.MoveNextAsync();
                    } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        throw ProviderStopped();
                    }
                    if (!moved) {
                        yield break;
                    }

                    silence.CancelAfter(allowed);
                    if (items.Current.Data == "[DONE]") {
                        yield break;
                    }

                    IReadOnlyList<ChatStreamEvent> events = ParsePayload(items.Current.Data, request.ReasoningField);
                    foreach (ChatStreamEvent @event in events) {
                        yield return @event;
                    }
                }
            }
        }
    }

    private static AssistantProviderException ProviderStopped() => new(
        "provider_timeout",
        "The provider stopped sending before the answer was complete.");

    private async Task<HttpResponseMessage> SendWithRetriesAsync(ChatRequest request, CancellationToken cancellationToken) {
        Uri endpoint = new(options.BaseUrl.TrimEnd('/') + "/" + options.ChatCompletionsPath.TrimStart('/'));
        for (int attempt = 0; ; attempt++) {
            using HttpRequestMessage message = new(HttpMethod.Post, endpoint) {
                Content = new StringContent(SerializeRequest(request), System.Text.Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (options.RequireApiKey && !string.IsNullOrWhiteSpace(options.ApiKey)) {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            HttpResponseMessage response;
            try {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            } catch (HttpRequestException exception) {
                throw new AssistantProviderException("provider_unreachable", "The configured provider could not be reached.", exception);
            }

            if (!IsRetryable(response.StatusCode) || attempt >= options.MaxProviderRetries) {
                return response;
            }

            TimeSpan delay = RetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static string SerializeRequest(ChatRequest request) {
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

    private static IReadOnlyList<ChatStreamEvent> ParsePayload(string data, string reasoningField) {
        JsonDocument document;
        try {
            document = JsonDocument.Parse(data);
        } catch (JsonException exception) {
            throw new AssistantProviderException("provider_invalid_response", "The provider returned an invalid streaming response.", exception);
        }

        using (document) {
            JsonElement root = document.RootElement;
            List<ChatStreamEvent> events = [];
            if (root.ValueKind != JsonValueKind.Object) {
                throw Invalid();
            }
            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0) {
                JsonElement choice = choices[0];
                if (choice.ValueKind != JsonValueKind.Object) {
                    throw Invalid();
                }
                if (choice.TryGetProperty("delta", out JsonElement delta)) {
                    // A delta that is not an object would make every property read throw; report the
                    // provider's shape instead of failing the run as an internal error.
                    if (delta.ValueKind != JsonValueKind.Object) {
                        throw Invalid();
                    }
                    if (delta.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) {
                        events.Add(new ContentDelta(content.GetString()!));
                    }
                    foreach (string field in ReasoningFields(reasoningField)) {
                        if (delta.TryGetProperty(field, out JsonElement reasoning) && reasoning.ValueKind == JsonValueKind.String) {
                            events.Add(new ReasoningDelta(reasoning.GetString()!));
                        }
                    }
                    if (delta.TryGetProperty("tool_calls", out JsonElement toolCalls) && toolCalls.ValueKind == JsonValueKind.Array) {
                        foreach (JsonElement toolCall in toolCalls.EnumerateArray()) {
                            if (toolCall.ValueKind != JsonValueKind.Object) {
                                throw Invalid();
                            }
                            int index = toolCall.TryGetProperty("index", out JsonElement indexValue)
                                && indexValue.TryGetInt32(out int parsedIndex) ? parsedIndex : 0;
                            string? id = StringProperty(toolCall, "id");
                            string? name = null;
                            string? arguments = null;
                            if (toolCall.TryGetProperty("function", out JsonElement function)) {
                                name = StringProperty(function, "name");
                                arguments = StringProperty(function, "arguments");
                            }
                            events.Add(new ToolCallDelta(index, id, name, arguments));
                        }
                    }
                }
                if (choice.TryGetProperty("finish_reason", out JsonElement finish) && finish.ValueKind == JsonValueKind.String) {
                    events.Add(new Finished(finish.GetString()));
                }
            }

            if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object) {
                events.Add(new UsageEvent(new TokenUsage(
                    IntProperty(usage, "prompt_tokens"),
                    IntProperty(usage, "completion_tokens"))));
            }
            return events;
        }
    }

    private static AssistantProviderException Invalid() =>
        new("provider_invalid_response", "The provider returned an invalid streaming response.");

    private static IEnumerable<string> ReasoningFields(string configured) {
        HashSet<string> fields = new(StringComparer.Ordinal) { "reasoning_content", "reasoning", configured };
        return fields;
    }

    private static string? StringProperty(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? IntProperty(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : null;

    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt) {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate) {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }
        if (retryAfter is { } requested) {
            return TimeSpan.FromMilliseconds(Math.Clamp(requested.TotalMilliseconds, 0, 30_000));
        }

        double backoff = Math.Min(5_000, 250 * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(backoff + Random.Shared.Next(0, 200));
    }

    private static AssistantProviderException ProviderError(HttpStatusCode status) => status switch {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new AssistantProviderException("provider_unauthorised", "The configured provider rejected the request."),
        HttpStatusCode.TooManyRequests =>
            new AssistantProviderException("provider_rate_limited", "The configured provider is rate limiting requests."),
        _ => new AssistantProviderException("provider_error", "The configured provider could not complete the request."),
    };
}
