// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>A run that runs out of a budget asks the owner, and carries on when they say yes.</summary>
[TestFixture]
public sealed class AssistantContinuationFlowTests : IntegrationTest {
    private readonly PlannedClient _provider = new();

    internal override AspApplication CreateApplication() =>
        // One step is the whole budget, so the run pauses after its tool call and the second script
        // is the answer the owner's continuation unlocks.
        new(assistantEnabled: true, chatCompletionClient: _provider, assistantMaxSteps: 1);

    [Test]
    public async Task AStepBudgetPausesTheRunAndContinueCarriesItOnAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", token),
            ("content", "Read the missing page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpRequestMessage eventRequest = new(HttpMethod.Get,
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        using HttpResponseMessage eventResponse = await client.SendAsync(eventRequest, HttpCompletionOption.ResponseHeadersRead);
        using StreamReader reader = new(await eventResponse.Content.ReadAsStreamAsync(), Encoding.UTF8);

        string paused = await ReadUntilEventAsync(reader, "run.paused");
        Assert.That(paused, Does.Contain("\"budget\":\"steps\""));
        Assert.That(paused, Does.Contain("used all 1 of its steps"), "the pause says which budget ran out");

        // The question outlives a reload: it is rendered from the run's stored status.
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string pending = await reloaded.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(pending, Does.Contain("Do you want to continue?"));
            Assert.That(pending, Does.Contain("used all 1 of its steps"));
            Assert.That(pending, Does.Contain($"/runs/{runId}/continue"));
        });

        string continueToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage continued = await client.PostAsync(
            $"{conversation}/runs/{runId}/continue", Form(("__RequestVerificationToken", continueToken)));
        Assert.That(continued.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        string finished = await ReadUntilEventAsync(reader, "run.completed");
        Assert.That(finished, Does.Contain("event: run.resumed"));

        // The answer the continuation unlocked is stored, and nothing was written for the pause itself.
        using HttpResponseMessage page = await client.GetAsync(conversation);
        string body = await page.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("The page is missing."));
            Assert.That(body, Does.Not.Contain("Do you want to continue?"), "the question is gone once it is answered");
            Assert.That(body, Does.Contain("id=\"assistant-budget-steps-limit\">2</span>"),
                "the steps budget grew by one window");
        });
    }

    [Test]
    public async Task ContinuingARunThatIsNotPausedIsRefusedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", token),
            ("content", "Read the missing page")));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        string continueToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage refused = await client.PostAsync(
            $"{conversation}/runs/{runId}/continue", Form(("__RequestVerificationToken", continueToken)));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain("not waiting to continue"));

        // Leave no run behind for the next fixture to trip over.
        string stopToken = await GetAntiforgeryTokenAsync(client, conversation);
        _ = await client.PostAsync(conversation + "/stop", Form(("__RequestVerificationToken", stopToken)));
    }

    private sealed class PlannedClient : IChatCompletionClient {
        private readonly IReadOnlyList<ChatStreamEvent>[] _responses = [
            [
                new ToolCallDelta(0, "read-call", "read_page", "{\"path\":\"/a-page-that-is-not-there\"}"),
                new Finished("tool_calls"),
            ],
            [new ContentDelta("The page is missing."), new Finished("stop")],
        ];
        private int _request;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            int index = Math.Min(Interlocked.Increment(ref _request) - 1, _responses.Length - 1);
            foreach (ChatStreamEvent @event in _responses[index]) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
