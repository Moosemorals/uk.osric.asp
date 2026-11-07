// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantProviderClientTests {

    // The timeout bounds silence between events, so the gaps in a test stream have to be well under
    // it: half a second of silence against 200 ms gaps leaves the same room a whole second left for
    // 600 ms ones, for half the wall clock. Four gaps make 800 ms of stream instead of 2.4 s.
    private const double TimeoutSeconds = 0.5;

    [Test]
    public async Task StreamsContentReasoningToolCallsAndUsageAsync() {
        string payload = """
            data: {"choices":[{"index":0,"delta":{"content":"Hello","reasoning_content":"Thinking","tool_calls":[{"index":0,"id":"call-1","function":{"name":"read_page","arguments":"{\"path\":"}}]},"finish_reason":null}]}

            data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"/home\"}"}}]},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":7,"completion_tokens":2}}

            data: [DONE]

            """;
        using HttpClient client = Client(_ => Task.FromResult(Response(HttpStatusCode.OK, payload)));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false }, NullLogger<OpenAiChatCompletionClient>.Instance);

        List<ChatStreamEvent> events = await CollectAsync(provider);

        Assert.That(events.OfType<ContentDelta>().Select(delta => delta.Text), Is.EqualTo(["Hello"]));
        Assert.That(events.OfType<ReasoningDelta>().Select(delta => delta.Text), Is.EqualTo(["Thinking"]));
        Assert.That(events.OfType<ToolCallDelta>().Select(delta => delta.Arguments), Is.EqualTo(["{\"path\":", "\"/home\"}"]));
        Assert.That(events.OfType<UsageEvent>().Single().Usage, Is.EqualTo(new TokenUsage(7, 2)));
        Assert.That(events.OfType<Finished>().Single().Reason, Is.EqualTo("tool_calls"));
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
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false }, NullLogger<OpenAiChatCompletionClient>.Instance);

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
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false, MaxProviderRetries = 1 }, NullLogger<OpenAiChatCompletionClient>.Instance);

        await CollectAsync(provider);

        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task MalformedPayloadFailsWithASafeProviderErrorAsync() {
        using HttpClient client = Client(_ => Task.FromResult(Response(HttpStatusCode.OK, "data: {not-json}\n\n")));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false }, NullLogger<OpenAiChatCompletionClient>.Instance);

        ProviderException exception = (await Assert.ThrowsAsync<ProviderException>(async () => await CollectAsync(provider)))!;

        Assert.That(exception.Code, Is.EqualTo("provider_invalid_response"));
        Assert.That(exception.Message, Does.Not.Contain("not-json"));
    }

    [Test]
    public async Task UnauthorizedResponseNeverExposesTheKeyOrProviderBodyAsync() {
        const string ApiKey = "private-test-key";
        using HttpClient client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) {
            Content = new StringContent("contains private provider details"),
        }));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { ApiKey = ApiKey }, NullLogger<OpenAiChatCompletionClient>.Instance);

        ProviderException exception = (await Assert.ThrowsAsync<ProviderException>(async () => await CollectAsync(provider)))!;

        Assert.That(exception.Code, Is.EqualTo("provider_unauthorised"));
        Assert.That(exception.Message, Does.Not.Contain(ApiKey));
        Assert.That(exception.Message, Does.Not.Contain("private provider details"));
    }

    [Test]
    public async Task AStreamThatKeepsSendingOutlivesTheRequestTimeoutAsync() {
        using HttpClient client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new TimedContent(
                ("data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"think \"}}]}\n\n", 200),
                ("data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"think \"}}]}\n\n", 200),
                ("data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"think \"}}]}\n\n", 200),
                ("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"done\"}}]}\n\n", 200),
                ("data: [DONE]\n\n", 0)),
        }));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false, RequestTimeoutSeconds = TimeoutSeconds }, NullLogger<OpenAiChatCompletionClient>.Instance);

        List<ChatStreamEvent> events = await CollectAsync(provider);

        Assert.That(events.OfType<ContentDelta>().Select(delta => delta.Text), Is.EqualTo(["done"]));
    }

    [Test]
    public async Task AKeepAliveCommentDoesNotResetTheRequestTimeoutAsync() {
        using HttpClient client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new TimedContent(
                (": keep-alive\n\n", 300),
                (": keep-alive\n\n", 300),
                (": keep-alive\n\n", 300),
                (": keep-alive\n\n", 300)),
        }));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false, RequestTimeoutSeconds = 1 }, NullLogger<OpenAiChatCompletionClient>.Instance);

        ProviderException exception = (await Assert.ThrowsAsync<ProviderException>(async () => await CollectAsync(provider)))!;

        Assert.That(exception.Code, Is.EqualTo("provider_timeout"));
    }

    [Test]
    public async Task AStreamThatStopsSendingFailsWithASafeTimeoutAsync() {
        using HttpClient client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new TimedContent(("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"starting\"}}]}\n\n", 4000)),
        }));
        OpenAiChatCompletionClient provider = new(client, new AssistantOptions { RequireApiKey = false, RequestTimeoutSeconds = TimeoutSeconds }, NullLogger<OpenAiChatCompletionClient>.Instance);

        ProviderException exception = (await Assert.ThrowsAsync<ProviderException>(async () => await CollectAsync(provider)))!;

        Assert.Multiple(() => {
            Assert.That(exception.Code, Is.EqualTo("provider_timeout"));
            Assert.That(exception.Message, Does.Contain("stopped sending"));
        });
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new RecordingHandler((request, _) => send(request))) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new RecordingHandler(send)) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private static async Task<List<ChatStreamEvent>> CollectAsync(IChatCompletionClient provider) {
        ChatRequest request = new("model", [new ChatMessage("system", "prompt")], [], 0.2, 20, "reasoning_content", true);
        List<ChatStreamEvent> events = [];
        await foreach (ChatStreamEvent @event in provider.StreamAsync(request, CancellationToken.None)) {
            events.Add(@event);
        }
        return events;
    }

    // A response body that trickles out over time, which is how a real provider streams: the default
    // HttpContent machinery buffers a response before handing out its stream, which would flatten it.
    private sealed class TimedContent(params (string Text, int DelayMilliseconds)[] parts) : HttpContent {
        private readonly Pipe _pipe = new();

        protected override Task<Stream> CreateContentReadStreamAsync() {
            _ = WriteAsync();
            return Task.FromResult(_pipe.Reader.AsStream());
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            CreateContentReadStreamAsync();

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new NotSupportedException("The timed content is only read as a stream.");

        protected override bool TryComputeLength(out long length) {
            length = 0;
            return false;
        }

        private async Task WriteAsync() {
            foreach ((string text, int delayMilliseconds) in parts) {
                if (delayMilliseconds > 0) {
                    await Task.Delay(delayMilliseconds);
                }
                await _pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(text));
            }
            await _pipe.Writer.CompleteAsync();
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
