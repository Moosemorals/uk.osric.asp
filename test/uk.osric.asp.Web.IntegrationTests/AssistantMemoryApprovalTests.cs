// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantMemoryApprovalTests : IntegrationTest {
    private readonly GuardedClient _provider = new();

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task ARequiredApprovalPausesTheWriteAndADenialStoresNothingAsync() {
        await RunGuardedAsync("memo-deny", decision: "deny");

        Assert.That(await MemoryCountAsync("memo-deny"), Is.Zero, "a denied write is not a stored memory");
    }

    [Test]
    public async Task AnApprovedWriteStoresUnderTheConversationsOwnerAsync() {
        await RunGuardedAsync("memo-approve", decision: "approve");

        Assert.That(await MemoryCountAsync("memo-approve"), Is.EqualTo(1));
    }

    private async Task RunGuardedAsync(string ownerName, string decision) {
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        using HttpClient client = await CreateSignedInClientAsync(ownerName);
        string conversation = await CreateConversationAsync(client);

        string modeToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage mode = await client.PostAsync(conversation + "/options", Form(
            ("__RequestVerificationToken", modeToken),
            ("title", ConversationTitle.NewConversation),
            ("mode", "RequireApproval")));
        Assert.That(mode.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Remember that I write British English.")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpRequestMessage eventRequest = new(HttpMethod.Get,
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        using HttpResponseMessage eventResponse = await client.SendAsync(eventRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.That(eventResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using Stream eventStream = await eventResponse.Content.ReadAsStreamAsync();
        using StreamReader reader = new(eventStream, Encoding.UTF8);

        string approvalEvent = await ReadUntilEventAsync(reader, "approval.required");
        Assert.Multiple(() => {
            Assert.That(approvalEvent, Does.Contain("remember"));
            Assert.That(approvalEvent, Does.Contain("British English"));
        });
        Assert.That(await MemoryCountAsync(ownerName), Is.Zero, "the write must wait for the owner");

        // The call the owner is asked about shows the exact content, so the decision is informed.
        using HttpResponseMessage pendingView = await client.GetAsync(conversation);
        string pendingBody = await pendingView.Content.ReadAsStringAsync();
        Assert.That(pendingBody, Does.Contain("British English"));

        string approvalPath = $"{conversation}/runs/{runId}/tool-calls/remember-1/approval";
        string pendingToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage resolved = await client.PostAsync(approvalPath, Form(
            ("__RequestVerificationToken", pendingToken),
            ("decision", decision)));
        Assert.That(resolved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        string completed = await ReadUntilEventAsync(reader, "run.completed");
        Assert.That(completed, Does.Contain("event: tool.finished"));
    }

    private async Task<int> MemoryCountAsync(string owner) =>
        await WithServicesAsync(provider => provider.GetRequiredService<IMemoryStore>().CountAsync(owner));

    private sealed class GuardedClient : IChatCompletionClient {
        private List<ChatStreamEvent[]> _responses = [];
        private int _request;

        public void Reset() {
            lock (_responses) {
                _responses = [
                    [
                        new ToolCallDelta(0, "remember-1", "remember", "{\"kind\":\"preference\",\"content\":\"The owner writes British English.\"}"),
                        new Finished("tool_calls"),
                    ],
                    [new ContentDelta("Done."), new Finished("stop")],
                ];
                _request = 0;
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            int index = Interlocked.Increment(ref _request) - 1;
            ChatStreamEvent[] events;
            lock (_responses) {
                Assert.That(index, Is.LessThan(_responses.Count), "the fixture scripted fewer responses than the runs asked for");
                events = _responses[index];
            }

            foreach (ChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
