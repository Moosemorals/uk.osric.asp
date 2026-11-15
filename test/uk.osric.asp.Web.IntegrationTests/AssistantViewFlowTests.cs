// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>The whole loop: a run writes, views what the server serves, and reads it back.</summary>
/// <remarks>
/// The outbound client is pointed at the test server, so a view is a real request through the real
/// pipeline — token, scheme, guard, routing, page — with no socket and no browser.
/// </remarks>
[TestFixture]
public sealed class AssistantViewFlowTests : IntegrationTest {

    private readonly PlannedClient _provider = new([
        // 1. Look at a page the site already serves.
        [
            new ToolCallDelta(0, "view-about", "view", """{"path":"/about"}"""),
            new Finished("tool_calls"),
        ],
        // 2. Look at the editor, which renders only for a signed-in owner.
        [
            new ToolCallDelta(1, "view-editor", "view", """{"path":"/pages?edit=/pages"}"""),
            new Finished("tool_calls"),
        ],
        [new ContentDelta("Both pages render."), new Finished("stop")],
    ]);

    internal override AspApplication CreateApplication() => new(
        chatCompletionClient: _provider,
        selfFetchToServer: true,
        settings: new Dictionary<string, string?> {
            // The test server answers on this origin, and it is the only one a view offers its token to.
            ["Site:Origin"] = "https://localhost",
        });

    [Test]
    public async Task ARunViewsTheSiteAsItsOwnerAndReadsWhatTheServerServedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        // The first render of the editor compiles 31 KB of template, which is longer than a fetch is
        // allowed; a real owner has opened the editor before a run looks at it.
        Assert.That((await client.GetAsync("/pages")).IsSuccessStatusCode, Is.True);
        string conversation = await CreateConversationAsync(client);
        int start = _provider.Requests.Count;

        string body = await RunAsync(client, conversation, "Look at the site");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("Both pages render."));
            Assert.That(_provider.Requests, Has.Count.EqualTo(start + 3), "two tool steps and a final answer");
            Assert.That(_provider.ToolResult(start + 2, "view-about"), Does.Contain("MIT"), "the about page's own text");
            Assert.That(_provider.ToolResult(start + 3, "view-editor"), Does.Contain("textarea"), "the editor, as its owner sees it");
        });
    }

    [Test]
    public async Task AViewedResponseCarriesTheHeadersAPageFetchWouldHaveThrownAwayAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        Assert.That((await client.GetAsync("/pages")).IsSuccessStatusCode, Is.True);
        string conversation = await CreateConversationAsync(client);

        int start = _provider.Requests.Count;
        _ = await RunAsync(client, conversation, "Look at the site");

        string result = _provider.ToolResult(start + 3, "view-editor");
        Assert.Multiple(() => {
            Assert.That(result, Does.Contain("Content-Security-Policy"));
            Assert.That(result, Does.Contain("X-Frame-Options"));
            Assert.That(result, Does.Contain("\"redirects\":[]"), "the owner's own view takes no hop to sign in");
        });
    }

    private async Task<string> RunAsync(HttpClient client, string conversation, string message) {
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        HttpResponseMessage submitted = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", token),
            ("content", message)));
        Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), await submitted.Content.ReadAsStringAsync());

        using JsonDocument run = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        // Reading the event stream is what waits for the run to finish.
        string events = await client.GetStringAsync($"{conversation}/events?run={runId}&after=0");
        string failure = string.Join("\n", Logs.Entries
            .Where(entry => entry.Exception is not null || entry.Level >= Microsoft.Extensions.Logging.LogLevel.Warning)
            .Select(entry => $"{entry.Level} {entry.Category}: {entry.Message} {entry.Exception}"));
        Assert.That(events, Does.Contain("event: run.completed"), events + "\n---\n" + failure);

        return await client.GetStringAsync(conversation);
    }

    private sealed class PlannedClient(IReadOnlyList<ChatStreamEvent>[] responses) : IChatCompletionClient {
        private int _request;

        /// <summary>Every request the run sent, in order.</summary>
        public List<ChatRequest> Requests { get; } = [];

        /// <summary>What the model was told for the tool call with this id, on this request.</summary>
        /// <param name="request">The 1-based request number.</param>
        /// <param name="toolCallId">The tool call whose result to read.</param>
        public string ToolResult(int request, string toolCallId) {
            ChatMessage? found = Requests[request - 1].Messages
                .LastOrDefault(message => string.Equals(message.ToolCallId, toolCallId, StringComparison.Ordinal));

            if (found is null) {
                Assert.Fail($"no tool result for {toolCallId} in request {request}:\n"
                    + string.Join("\n", Requests[request - 1].Messages
                        .Select(message => $"[{message.Role}] id={message.ToolCallId} name={message.ToolName}")));
            }

            return found!.Content;
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            int index = Interlocked.Increment(ref _request) - 1;
            Requests.Add(request);
            // Each run starts at the first step, so a fixture with two tests repeats its script.
            foreach (ChatStreamEvent @event in responses[index % responses.Length]) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
