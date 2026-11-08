// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantFlowTests : IntegrationTest {
    private readonly ScriptedClient _provider = new([
        new ContentDelta("Hello from the assistant."),
        new Finished("stop"),
    ]);

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task SignedInOwnerCanCreateSendStreamAndReloadAConversationAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        using HttpResponseMessage assistant = await client.GetAsync("/assistant");
        Assert.That(assistant.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(assistant.Headers.CacheControl?.NoStore, Is.True);

        string createToken = await GetAntiforgeryTokenAsync(client, "/assistant");
        using HttpResponseMessage created = await client.PostAsync("/assistant", Form(
            ("__RequestVerificationToken", createToken)));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        string location = created.Headers.Location!.ToString();

        using HttpResponseMessage chat = await client.GetAsync(location);
        string chatBody = await chat.Content.ReadAsStringAsync();
        Assert.That(chat.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(chatBody, Does.Contain("Always approve"));
        Assert.That(chatBody, Does.Contain("/assistant.js"));
        string messageToken = await GetAntiforgeryTokenAsync(client, location);
        using HttpResponseMessage submitted = await client.PostAsync(location + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Say hello")));
        Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync($"{location}/events?run={runId}&after=0");
        string eventStream = await events.Content.ReadAsStringAsync();
        Assert.That(events.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(events.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        Assert.That(eventStream, Does.Contain("event: content.delta"));
        Assert.That(eventStream, Does.Contain("Hello from the assistant."));
        Assert.That(eventStream, Does.Contain("event: run.completed"));

        using HttpResponseMessage reloaded = await client.GetAsync(location);
        string reloadedBody = await reloaded.Content.ReadAsStringAsync();
        Assert.That(reloadedBody, Does.Contain("Hello from the assistant."));
        Assert.That(reloadedBody, Does.Contain("Say hello"));
        Assert.That(_provider.RequestCount, Is.EqualTo(1));
    }

    private sealed class ScriptedClient(IReadOnlyList<ChatStreamEvent> events) : IChatCompletionClient {
        private int _requestCount;

        public int RequestCount => _requestCount;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            Interlocked.Increment(ref _requestCount);
            foreach (ChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
