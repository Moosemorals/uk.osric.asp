// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;

/// <summary>Streams completions from an OpenAI-compatible chat-completions endpoint.</summary>
internal sealed class OpenAiChatCompletionClient(
    HttpClient client,
    AssistantOptions options,
    ILogger<OpenAiChatCompletionClient> logger) : IChatCompletionClient {

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
            // The body is built once, outside the retry loop: the meter the planner used and the bytes
            // that go on the wire are the same string, and a retry resends it unchanged.
            response = await SendWithRetriesAsync(ChatRequestJson.Serialize(request), silence.Token);
            if (!response.IsSuccessStatusCode) {
                // The status is the whole diagnosis for a rejected request - a wrong model name, a
                // revoked key, a quota - and the body that would say more is never logged, because a
                // provider is free to quote the prompt back in it.
                logger.LogWarning("The provider answered {StatusCode} for model {Model}", (int)response.StatusCode, request.Model);
                ProviderException failure = ProviderError(response.StatusCode);
                response.Dispose();
                throw failure;
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

    private static ProviderException ProviderStopped() => new(
        "provider_timeout",
        "The provider stopped sending before the answer was complete.");

    private async Task<HttpResponseMessage> SendWithRetriesAsync(string body, CancellationToken cancellationToken) {
        Uri endpoint = new(options.BaseUrl.TrimEnd('/') + "/" + options.ChatCompletionsPath.TrimStart('/'));
        for (int attempt = 0; ; attempt++) {
            using HttpRequestMessage message = new(HttpMethod.Post, endpoint) {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (options.RequireApiKey && !string.IsNullOrWhiteSpace(options.ApiKey)) {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            HttpResponseMessage response;
            try {
                response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            } catch (HttpRequestException exception) {
                throw new ProviderException("provider_unreachable", "The configured provider could not be reached.", exception);
            }

            if (!IsRetryable(response.StatusCode) || attempt >= options.MaxProviderRetries) {
                return response;
            }

            TimeSpan delay = RetryDelay(response, attempt);
            logger.LogDebug("The provider answered {StatusCode} on attempt {Attempt}; retrying in {DelayMs:0} ms",
                (int)response.StatusCode, attempt + 1, delay.TotalMilliseconds);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static IReadOnlyList<ChatStreamEvent> ParsePayload(string data, string reasoningField) {
        JsonDocument document;
        try {
            document = JsonDocument.Parse(data);
        } catch (JsonException exception) {
            throw new ProviderException("provider_invalid_response", "The provider returned an invalid streaming response.", exception);
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
                // DeepSeek reports the cache hit at the top level; OpenAI reports it under
                // prompt_tokens_details. Both count the cached part of prompt_tokens.
                int? cached = IntProperty(usage, "prompt_cache_hit_tokens")
                    ?? NestedIntProperty(usage, "prompt_tokens_details", "cached_tokens");
                int? reasoning = NestedIntProperty(usage, "completion_tokens_details", "reasoning_tokens");
                events.Add(new UsageEvent(new TokenUsage(
                    IntProperty(usage, "prompt_tokens"),
                    IntProperty(usage, "completion_tokens"),
                    cached,
                    reasoning)));
            }
            return events;
        }
    }

    private static ProviderException Invalid() =>
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

    private static int? NestedIntProperty(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? IntProperty(nested, property)
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

    private static ProviderException ProviderError(HttpStatusCode status) => status switch {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new ProviderException("provider_unauthorised", "The configured provider rejected the request."),
        HttpStatusCode.TooManyRequests =>
            new ProviderException("provider_rate_limited", "The configured provider is rate limiting requests."),
        _ => new ProviderException("provider_error", "The configured provider could not complete the request."),
    };
}
