// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>A run asks its owner a question, stops until they answer, and carries on in the same step.</summary>
[TestFixture]
public sealed class AssistantQuestionFlowTests : IntegrationTest {
    private readonly AskingClient _provider = new();

    internal override AspApplication CreateApplication() => new(chatCompletionClient: _provider);

    [Test]
    public async Task AQuestionPausesTheRunUntilTheOwnerAnswersAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);

        string asking = await ReadUntilEventAsync(reader, "interrupt.required");
        Assert.Multiple(() => {
            Assert.That(asking, Does.Contain("\"kind\":\"question\""));
            Assert.That(asking, Does.Contain("Which heading should the page use?"));
            Assert.That(asking, Does.Contain("\"toolCallId\":\"ask-call\""));
        });

        // The card is rendered from the stored request, so a reload while the run waits shows it.
        using HttpResponseMessage pending = await client.GetAsync(conversation);
        string pendingBody = await pending.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(pendingBody, Does.Contain("section class=\"assistant-question\""));
            Assert.That(pendingBody, Does.Contain("Which heading should the page use?"));
            Assert.That(pendingBody, Does.Contain("name=\"answer0\" value=\"Alpha\""));
            Assert.That(pendingBody, Does.Contain("name=\"answer0\" value=\"Beta\""));
            Assert.That(pendingBody, Does.Contain("name=\"other1\""));
            Assert.That(pendingBody, Does.Contain("name=\"toolCallId\" value=\"ask-call\""));
        });

        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage answered = await client.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", token),
                ("toolCallId", "ask-call"),
                ("answer0", "Alpha"),
                ("other1", "About us")));
        Assert.That(answered.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        string completed = await ReadUntilEventAsync(reader, "run.completed");
        Assert.That(completed, Does.Contain("event: interrupt.resolved"));
        _ = await reader.ReadLineAsync();

        // The answer is the call's result, so the next request the provider sees carries it.
        ChatMessage sent = _provider.Requests[^1].Messages[^1];
        Assert.Multiple(() => {
            Assert.That(sent.Role, Is.EqualTo("tool"));
            Assert.That(sent.ToolCallId, Is.EqualTo("ask-call"));
            Assert.That(sent.Content, Does.Contain("\"selected\":[\"Alpha\"]"));
            Assert.That(sent.Content, Does.Contain("\"text\":\"About us\""));
        });

        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("section class=\"assistant-question-answers\""));
            Assert.That(body, Does.Contain("Which heading should the page use?"));
            Assert.That(body, Does.Contain("<dd>Alpha</dd>"));
            Assert.That(body, Does.Contain("<dd>About us</dd>"));
            Assert.That(body, Does.Not.Contain("section class=\"assistant-question\""), "the card is gone once answered");
        });
    }

    [Test]
    public async Task TheQuestionCardSurvivesAReloadWhileTheRunWaitsAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);
        _ = await ReadUntilEventAsync(reader, "interrupt.required");

        using HttpResponseMessage first = await client.GetAsync(conversation);
        using HttpResponseMessage second = await client.GetAsync(conversation);
        string firstBody = await first.Content.ReadAsStringAsync();
        string secondBody = await second.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(firstBody, Does.Contain("section class=\"assistant-question\""));
            Assert.That(secondBody, Does.Contain("section class=\"assistant-question\""), "a reload shows the card again");
            Assert.That(secondBody, Does.Contain($"/runs/{runId}/interrupt"));
        });

        await StopAsync(client, conversation);
    }

    [Test]
    public async Task AnAnswerToASettledQuestionIsRefusedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);
        _ = await ReadUntilEventAsync(reader, "interrupt.required");

        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage answered = await client.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", token),
                ("toolCallId", "ask-call"),
                ("answer0", "Alpha")));
        Assert.That(answered.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _ = await ReadUntilEventAsync(reader, "run.completed");
        _ = await reader.ReadLineAsync();

        string lateToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage again = await client.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", lateToken),
                ("toolCallId", "ask-call"),
                ("answer0", "Beta")));

        string refusal = await again.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(refusal, Does.Contain("no longer waiting"));
        });
    }

    [Test]
    public async Task AnOwnerMaySkipEveryQuestionAndTheRunContinuesAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);
        _ = await ReadUntilEventAsync(reader, "interrupt.required");

        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage skipped = await client.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", token),
                ("toolCallId", "ask-call")));
        Assert.That(skipped.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _ = await ReadUntilEventAsync(reader, "run.completed");
        _ = await reader.ReadLineAsync();

        ChatMessage sent = _provider.Requests[^1].Messages[^1];
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(sent.Content, Does.Contain("\"selected\":[]"));
            Assert.That(sent.Content, Does.Contain("\"text\":\"\""));
            Assert.That(body, Does.Contain("assistant-question-unanswered"));
        });
    }

    [Test]
    public async Task AnotherAccountsAnswerIsNotFoundAsync() {
        await SeedAdditionalUserAsync("other", Password);
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);
        _ = await ReadUntilEventAsync(reader, "interrupt.required");

        using HttpClient other = await CreateSignedInClientAsync("other", Password);
        // A token is bound to the account that asked for it, so the stranger's own page supplies it.
        string token = await GetAntiforgeryTokenAsync(other, "/assistant");
        using HttpResponseMessage stranger = await other.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", token),
                ("toolCallId", "ask-call"),
                ("answer0", "Alpha")));

        Assert.That(stranger.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        await StopAsync(client, conversation);
    }

    [Test]
    public async Task AnAnswerOverTheMessageBudgetIsRefusedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        (string runId, StreamReader reader) = await StartAsync(client, conversation);
        _ = await ReadUntilEventAsync(reader, "interrupt.required");

        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage tooLong = await client.PostAsync(
            $"{conversation}/runs/{runId}/interrupt", Form(
                ("__RequestVerificationToken", token),
                ("toolCallId", "ask-call"),
                ("other1", new string('a', 190_000))));

        string refusal = await tooLong.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(refusal, Does.Contain("limit is"));
        });
        await StopAsync(client, conversation);
    }

    private static async Task<(string RunId, StreamReader Reader)> StartAsync(HttpClient client, string conversation) {
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", token),
            ("content", "Make the page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpRequestMessage request = new(HttpMethod.Get,
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return (runId, new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8));
    }

    private static async Task StopAsync(HttpClient client, string conversation) {
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        _ = await client.PostAsync(conversation + "/stop", Form(("__RequestVerificationToken", token)));
    }

    /// <summary>
    /// Asks once per run and finishes once the answer comes back. It reads the transcript rather than
    /// counting requests, so every test in the fixture gets the run it scripted.
    /// </summary>
    private sealed class AskingClient : IChatCompletionClient {

        private readonly List<ChatRequest> _requests = [];

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

            IReadOnlyList<ChatStreamEvent> response = request.Messages[^1].Role == "tool"
                ? [new ContentDelta("Thanks."), new Finished("stop")]
                : [
                    new ToolCallDelta(0, "ask-call", "ask_user", """
                        {"questions":[
                          {"question":"Which heading should the page use?","header":"Heading",
                           "options":[{"label":"Alpha","description":"The first"},{"label":"Beta"}]},
                          {"question":"Anything else?"}]}
                        """),
                    new Finished("tool_calls"),
                ];
            foreach (ChatStreamEvent @event in response) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
