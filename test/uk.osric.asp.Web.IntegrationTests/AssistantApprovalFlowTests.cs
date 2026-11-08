// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantApprovalFlowTests : IntegrationTest {
    private readonly PlannedClient _provider = new();

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task RequiredApprovalPausesWritesAndAlwaysApproveExecutesWithoutPromptAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string gatedConversation = await CreateConversationAsync(client);
        string modeToken = await GetAntiforgeryTokenAsync(client, gatedConversation);
        using HttpResponseMessage mode = await client.PostAsync(gatedConversation + "/options", Form(
            ("__RequestVerificationToken", modeToken),
            ("title", ConversationTitle.NewConversation),
            ("mode", "RequireApproval")));
        Assert.That(mode.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        string messageToken = await GetAntiforgeryTokenAsync(client, gatedConversation);
        using HttpResponseMessage started = await client.PostAsync(gatedConversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Write a page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpRequestMessage eventRequest = new(HttpMethod.Get,
            $"{gatedConversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        using HttpResponseMessage eventResponse = await client.SendAsync(eventRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.That(eventResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using Stream eventStream = await eventResponse.Content.ReadAsStreamAsync();
        using StreamReader reader = new(eventStream, Encoding.UTF8);
        string approvalEvent = await ReadUntilEventAsync(reader, "approval.required");
        Assert.That(approvalEvent, Does.Contain("/gated-page"));
        Assert.That(await ReadPageAsync("/gated-page"), Is.Null, "the write must wait for the owner");

        string pendingToken = await GetAntiforgeryTokenAsync(client, gatedConversation);
        using HttpResponseMessage pendingView = await client.GetAsync(gatedConversation);
        string pendingBody = await pendingView.Content.ReadAsStringAsync();
        Assert.That(pendingBody, Does.Contain("write_page"));
        Assert.That(pendingBody, Does.Contain("&lt;h1&gt;Gated&lt;/h1&gt;"));

        string approvalPath = $"{gatedConversation}/runs/{runId}/tool-calls/gated-call/approval";
        using HttpResponseMessage approved = await client.PostAsync(approvalPath, Form(
            ("__RequestVerificationToken", pendingToken),
            ("decision", "approve")));
        Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        string completedEvents = await ReadUntilEventAsync(reader, "run.completed");
        Assert.That(completedEvents, Does.Contain("event: tool.finished"));
        Assert.That((await ReadPageAsync("/gated-page"))?.Template, Is.EqualTo("<h1>Gated</h1>"));

        // The turn has ended, so the transcript now says which page it changed, from the stored
        // result the real executor wrote rather than a hand-built copy of it.
        using HttpResponseMessage finished = await client.GetAsync(gatedConversation);
        string finishedBody = await finished.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(finishedBody, Does.Contain("assistant-changed-pages"), "the changed-pages card");
            Assert.That(finishedBody, Does.Contain("href=\"/gated-page\""), "the live page");
            Assert.That(finishedBody, Does.Contain("href=\"/edit/gated-page\""), "the editor");
        });

        string automaticConversation = await CreateConversationAsync(client);
        string automaticToken = await GetAntiforgeryTokenAsync(client, automaticConversation);
        using HttpResponseMessage automaticStarted = await client.PostAsync(automaticConversation + "/messages", Form(
            ("__RequestVerificationToken", automaticToken),
            ("content", "Write without a prompt")));
        using JsonDocument automaticRun = JsonDocument.Parse(await automaticStarted.Content.ReadAsStringAsync());
        string automaticRunId = automaticRun.RootElement.GetProperty("runId").GetString()!;
        using HttpResponseMessage automaticEvents = await client.GetAsync(
            $"{automaticConversation}/events?run={Uri.EscapeDataString(automaticRunId)}&after=0");
        string automaticBody = await automaticEvents.Content.ReadAsStringAsync();

        Assert.That(automaticStarted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(automaticBody, Does.Not.Contain("event: approval.required"));
        Assert.That(automaticBody, Does.Contain("event: run.completed"));
        Assert.That((await ReadPageAsync("/automatic-page"))?.Template, Is.EqualTo("<h1>Automatic</h1>"));
    }

    [Test]
    public async Task AnonymousSseRequestAnswersUnauthorizedInsteadOfRedirectingAsync() {
        using HttpClient anonymous = CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/assistant/1/events");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    private sealed class PlannedClient : IChatCompletionClient {
        private readonly IReadOnlyList<ChatStreamEvent>[] _responses = [
            [
                new ToolCallDelta(0, "gated-call", "write_page", "{\"path\":\"/gated-page\",\"content\":\"<h1>Gated</h1>\"}"),
                new Finished("tool_calls"),
            ],
            [new ContentDelta("The gated page is ready."), new Finished("stop")],
            [
                new ToolCallDelta(0, "automatic-call", "write_page", "{\"path\":\"/automatic-page\",\"content\":\"<h1>Automatic</h1>\"}"),
                new Finished("tool_calls"),
            ],
            [new ContentDelta("The automatic page is ready."), new Finished("stop")],
        ];
        private int _request;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            int index = Interlocked.Increment(ref _request) - 1;
            foreach (ChatStreamEvent @event in _responses[index]) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
