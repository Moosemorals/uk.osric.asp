// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>Proves that masking changes the request and leaves the stored transcript alone.</summary>
[TestFixture]
public sealed class AssistantContextMaskingTests : IntegrationTest {

    // Ten read_page results of about 13 kB each: eight fit the request budget, the ninth does not, so
    // the run has to elide its oldest observation to send the next step.
    private const int ToolCalls = 10;
    private const int Budget = 160_000;

    private readonly ReadingClient _provider = new(ToolCalls);

    internal override AspApplication CreateApplication() =>
        new(assistantEnabled: true, chatCompletionClient: _provider, maxRequestBytes: Budget);

    [Test]
    public async Task AnOldToolResultIsMaskedInTheRequestButNotInTheTranscript() {
        string source = "BEGIN-OF-BIG-PAGE\n"
            + string.Join('\n', Enumerable.Repeat("A line of the big page.", 2_000))
            + "\nEND-OF-BIG-PAGE";
        await SavePageAsync("/big", source);

        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string eventStream = await RunAsync(client, conversation);

        Assert.Multiple(() => {
            Assert.That(eventStream, Does.Contain("event: run.completed"));
            Assert.That(eventStream, Does.Contain("context_masked"), "the run had to shorten an old observation");
            Assert.That(_provider.Requests.Any(request => request.Messages.Any(message =>
                    message.Role == "tool" && message.Content.StartsWith("[tool result elided:", StringComparison.Ordinal))),
                Is.True, "a request sent an elided result");
        });

        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        int appearances = body.Split("BEGIN-OF-BIG-PAGE", StringSplitOptions.None).Length - 1;
        Assert.That(appearances, Is.EqualTo(ToolCalls),
            "the page still shows every full stored result, not the mask the provider received");
    }

    private static async Task<string> RunAsync(HttpClient client, string conversation) {
        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Read the big page ten times")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync(
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        return await events.Content.ReadAsStringAsync();
    }

    private sealed class ReadingClient(int toolCalls) : IChatCompletionClient {
        private readonly List<ChatRequest> _requests = [];
        private int _request;

        public IReadOnlyList<ChatRequest> Requests {
            get {
                lock (_requests) {
                    return [.. _requests];
                }
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            lock (_requests) {
                _requests.Add(request);
            }

            int index = Interlocked.Increment(ref _request);
            IReadOnlyList<ChatStreamEvent> events = index <= toolCalls
                ? [new ToolCallDelta(0, $"call-{index}", "read_page", """{"path":"/big"}"""), new Finished("tool_calls")]
                : [new ContentDelta("I read the big page many times."), new Finished("stop")];
            foreach (ChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}

/// <summary>A request that could never fit is refused before it reaches the provider.</summary>
[TestFixture]
public sealed class AssistantOverBudgetTests : IntegrationTest {

    private readonly CountingClient _provider = new();

    internal override AspApplication CreateApplication() =>
        new(assistantEnabled: true, chatCompletionClient: _provider, maxRequestBytes: 16_384);

    [Test]
    public async Task ARunThatCannotFitFailsWithContextTooLong() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Say hello")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync(
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        string eventStream = await events.Content.ReadAsStringAsync();

        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(eventStream, Does.Contain("context_too_long"));
            Assert.That(_provider.RequestCount, Is.Zero, "a request that cannot fit is never sent");
            Assert.That(body, Does.Contain("assistant-budget-exceeded"),
                "the page still shows the over-budget request rather than nothing");
        });
    }

    private sealed class CountingClient : IChatCompletionClient {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            Interlocked.Increment(ref _requestCount);
            await Task.CompletedTask;
            yield break;
        }
    }
}
