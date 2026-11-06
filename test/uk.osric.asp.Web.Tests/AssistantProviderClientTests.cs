// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantProviderClientTests {

    [Test]
    public async Task StreamsContentReasoningToolCallsAndUsageAsync() {
        string payload = """
            data: {"choices":[{"index":0,"delta":{"content":"Hello","reasoning_content":"Thinking","tool_calls":[{"index":0,"id":"call-1","function":{"name":"read_page","arguments":"{\"path\":"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"/home\"}"}}]},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":7,"completion_tokens":2}}

            data: [DONE]

            """;
        using HttpClient client = Client(_ => Task.FromResult(Response(HttpStatusCode.OK, payload)));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false });

        List<AssistantChatStreamEvent> events = await CollectAsync(provider);

        Assert.That(events.OfType<AssistantContentDelta>().Select(delta => delta.Text), Is.EqualTo(["Hello"]));
        Assert.That(events.OfType<AssistantReasoningDelta>().Select(delta => delta.Text), Is.EqualTo(["Thinking"]));
        Assert.That(events.OfType<AssistantToolCallDelta>().Select(delta => delta.Arguments), Is.EqualTo(["{\"path\":", "\"/home\"}"]));
        Assert.That(events.OfType<AssistantUsageEvent>().Single().Usage, Is.EqualTo(new AssistantUsage(7, 2)));
        Assert.That(events.OfType<AssistantFinished>().Single().Reason, Is.EqualTo("tool_calls"));
    }

    [Test]
    public async Task RequestUsesOpenAiCompatibleWireShapeAndDoesNotSendAnOptionalKeyAsync() {
        HttpRequestMessage? captured = null;
        string? body = null;
        using HttpClient client = Client(async (request, cancellationToken) => {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Response(HttpStatusCode.OK, "data: [DONE]\n\n");
        });
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false });

        await CollectAsync(provider);

        Assert.Multiple(() => {
            Assert.That(captured!.RequestUri!.AbsoluteUri, Is.EqualTo("https://api.deepseek.com/chat/completions"));
            Assert.That(captured.Headers.Accept, Does.Contain(new MediaTypeWithQualityHeaderValue("text/event-stream")));
            Assert.That(captured.Headers.Authorization, Is.Null);
            Assert.That(body, Does.Contain("\"stream\":true"));
            Assert.That(body, Does.Contain("\"tool_choice\":\"auto\""));
            Assert.That(body, Does.Contain("\"max_tokens\":20"));
        });
    }

    [Test]
    public async Task RetriesRateLimitBeforeTheStreamStartsAsync() {
        int calls = 0;
        using HttpClient client = Client(_ => {
            calls++;
            HttpResponseMessage response = calls == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Response(HttpStatusCode.OK, "data: [DONE]\n\n");
            if (calls == 1) {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            }
            return Task.FromResult(response);
        });
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false, MaxProviderRetries = 1 });

        await CollectAsync(provider);

        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task MalformedPayloadFailsWithASafeProviderErrorAsync() {
        using HttpClient client = Client(_ => Task.FromResult(Response(HttpStatusCode.OK, "data: {not-json}\n\n")));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false });

        AssistantProviderException exception = (await Assert.ThrowsAsync<AssistantProviderException>(async () => await CollectAsync(provider)))!;

        Assert.That(exception.Code, Is.EqualTo("provider_invalid_response"));
        Assert.That(exception.Message, Does.Not.Contain("not-json"));
    }

    [Test]
    public async Task UnauthorizedResponseNeverExposesTheKeyOrProviderBodyAsync() {
        const string ApiKey = "private-test-key";
        using HttpClient client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) {
            Content = new StringContent("contains private provider details"),
        }));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { ApiKey = ApiKey });

        AssistantProviderException exception = (await Assert.ThrowsAsync<AssistantProviderException>(async () => await CollectAsync(provider)))!;

        Assert.That(exception.Code, Is.EqualTo("provider_unauthorised"));
        Assert.That(exception.Message, Does.Not.Contain(ApiKey));
        Assert.That(exception.Message, Does.Not.Contain("private provider details"));
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new RecordingHandler((request, _) => send(request))) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new RecordingHandler(send)) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private static async Task<List<AssistantChatStreamEvent>> CollectAsync(IChatCompletionClient provider) {
        AssistantChatRequest request = new("model", [new AssistantChatMessage("system", "prompt")], [], 0.2, 20, "reasoning_content", true);
        List<AssistantChatStreamEvent> events = [];
        await foreach (AssistantChatStreamEvent @event in provider.StreamAsync(request, CancellationToken.None)) {
            events.Add(@event);
        }
        return events;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
