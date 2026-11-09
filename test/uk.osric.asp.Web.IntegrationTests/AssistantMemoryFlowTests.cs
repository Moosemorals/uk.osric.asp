// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class AssistantMemoryFlowTests : IntegrationTest {
    private readonly ScriptedMemoryClient _provider = new();

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task AMemoryWrittenInOneConversationReachesTheNextAndNeverAnotherOwnerAsync() {
        const string ownerName = "memo-flow";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        await SeedAdditionalUserAsync("memo-other", Password);
        _provider.Script(
            [Call(1, "remember-1", "remember", "{\"kind\":\"preference\",\"content\":\"The owner writes British English.\"}")],
            [new ContentDelta("Noted."), new Finished("stop")],
            [new ContentDelta("Hello again."), new Finished("stop")],
            [new ContentDelta("Hello stranger."), new Finished("stop")]);

        using HttpClient owner = await CreateSignedInClientAsync(ownerName);
        string first = await CreateConversationAsync(owner);
        await SendAsync(owner, first, "Please remember that I write British English.");

        // Recall is a keyword search, so the second turn has to name what the memory is about.
        string second = await CreateConversationAsync(owner);
        await SendAsync(owner, second, "Do I write British English?");

        using HttpClient other = await CreateSignedInClientAsync("memo-other");
        string theirs = await CreateConversationAsync(other);
        await SendAsync(other, theirs, "Do I write British English?");

        int ownerMemories = await MemoryCountAsync(ownerName);
        int otherMemories = await MemoryCountAsync("memo-other");
        IReadOnlyList<string> prompts = _provider.SystemPrompts;
        Assert.Multiple(() => {
            Assert.That(prompts, Has.Count.EqualTo(4));
            Assert.That(prompts[0], Does.Not.Contain("<memory id="), "the writing run had nothing to recall");
            Assert.That(prompts[2], Does.Contain("<memory id="));
            Assert.That(prompts[2], Does.Contain("The owner writes British English."));
            Assert.That(prompts[3], Does.Not.Contain("<memory id="), "another owner's memory is never in the prompt");
            Assert.That(ownerMemories, Is.EqualTo(1));
            Assert.That(otherMemories, Is.Zero);
        });
    }

    [Test]
    public async Task AForgetInALaterTurnRemovesTheMemoryFromTheNextConversationAsync() {
        const string ownerName = "memo-forget";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        _provider.Script(
            [Call(1, "remember-1", "remember", "{\"kind\":\"fact\",\"content\":\"The site runs on Podman.\"}")],
            [new ContentDelta("Noted."), new Finished("stop")],
            [Call(1, "forget-1", "forget_memory", string.Empty)],
            [new ContentDelta("Forgotten."), new Finished("stop")],
            [new ContentDelta("Hello again."), new Finished("stop")]);

        using HttpClient owner = await CreateSignedInClientAsync(ownerName);
        string first = await CreateConversationAsync(owner);
        await SendAsync(owner, first, "Remember that the site runs on Podman.");
        long id = await MemoryIdAsync(ownerName);
        _provider.Replace("forget-1", $"{{\"id\":{id}}}");

        string second = await CreateConversationAsync(owner);
        await SendAsync(owner, second, "Forget what you know about the runtime.");
        string third = await CreateConversationAsync(owner);
        await SendAsync(owner, third, "Where does the site run?");

        int held = await MemoryCountAsync(ownerName);
        IReadOnlyList<string> prompts = _provider.SystemPrompts;
        Assert.Multiple(() => {
            Assert.That(held, Is.Zero);
            Assert.That(prompts[^1], Does.Not.Contain("<memory id="));
        });
    }

    [Test]
    public async Task ADuplicateRememberStaysOneRowAsync() {
        const string ownerName = "memo-duplicate";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        _provider.Script(
            [Call(1, "remember-1", "remember", "{\"kind\":\"preference\",\"content\":\"Prefers colour over color.\"}")],
            [new ContentDelta("Noted."), new Finished("stop")],
            [Call(1, "remember-2", "remember", "{\"kind\":\"preference\",\"content\":\"prefers  colour over color.\"}")],
            [new ContentDelta("Already noted."), new Finished("stop")]);

        using HttpClient owner = await CreateSignedInClientAsync(ownerName);
        string first = await CreateConversationAsync(owner);
        await SendAsync(owner, first, "Remember I prefer colour over color.");
        string second = await CreateConversationAsync(owner);
        await SendAsync(owner, second, "Remember I prefer colour over color.");

        Assert.That(await MemoryCountAsync(ownerName), Is.EqualTo(1));
    }

    [Test]
    public async Task AWriteOverTheCapIsARefusalTheRunSurvivesAsync() {
        const string ownerName = "memo-full";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        await FillMemoryAsync(ownerName, 200);
        _provider.Script(
            [Call(1, "remember-1", "remember", "{\"kind\":\"fact\",\"content\":\"One fact too many.\"}")],
            [new ContentDelta("I could not remember that."), new Finished("stop")]);

        using HttpClient owner = await CreateSignedInClientAsync(ownerName);
        string conversation = await CreateConversationAsync(owner);
        string events = await SendAsync(owner, conversation, "Remember this one extra fact.");

        int held = await MemoryCountAsync(ownerName);
        Assert.Multiple(() => {
            Assert.That(events, Does.Contain("event: run.completed"));
            Assert.That(events, Does.Not.Contain("event: run.failed"));
            Assert.That(events, Does.Contain("MaxMemoriesPerOwner"));
            Assert.That(held, Is.EqualTo(200));
        });
    }

    private static ChatStreamEvent Call(int index, string id, string name, string arguments) =>
        new ToolCallDelta(index, id, name, arguments);

    private static async Task<string> SendAsync(HttpClient client, string conversation, string content) {
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage submitted = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", token),
            ("content", content)));
        Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        using JsonDocument run = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;
        using HttpResponseMessage events = await client.GetAsync($"{conversation}/events?run={runId}&after=0");
        Assert.That(events.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return await events.Content.ReadAsStringAsync();
    }

    private async Task<int> MemoryCountAsync(string owner) =>
        await WithServicesAsync(provider => provider.GetRequiredService<IMemoryStore>().CountAsync(owner));

    private async Task<long> MemoryIdAsync(string owner) =>
        await WithServicesAsync(async provider => {
            IReadOnlyList<MemoryCandidate> held = await provider.GetRequiredService<IMemoryStore>()
                .RecallAsync(owner, "\"podman\"*", 10);
            Assert.That(held, Is.Not.Empty);
            return held[0].Id;
        });

    // A store at the cap is what the tool refuses past, and writing the rows directly is the one way
    // to reach it without the test spending two hundred runs.
    private Task FillMemoryAsync(string owner, int count) =>
        WithServicesAsync(async provider => {
            StorageDbContext database = provider.GetRequiredService<StorageDbContext>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            for (int index = 0; index < count; index++) {
                database.Add(new MemoryEntry {
                    Owner = owner,
                    Kind = "fact",
                    Content = $"Seeded fact {index}.",
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            await database.SaveChangesAsync();
            return count;
        });

    private sealed class ScriptedMemoryClient : IChatCompletionClient {
        private readonly List<ChatStreamEvent[]> _responses = [];
        private readonly List<string> _systemPrompts = [];
        private int _request;

        public IReadOnlyList<string> SystemPrompts {
            get {
                lock (_systemPrompts) {
                    return [.. _systemPrompts];
                }
            }
        }

        /// <summary>Forgets every scripted response and every recorded prompt.</summary>
        /// <remarks>The fixture shares one host and one provider, so a test that counted requests would
        /// otherwise count the requests the tests before it made.</remarks>
        public void Reset() {
            lock (_responses) {
                _responses.Clear();
                _systemPrompts.Clear();
                Interlocked.Exchange(ref _request, 0);
            }
        }

        public void Script(params ChatStreamEvent[][] responses) {
            lock (_responses) {
                _responses.AddRange(responses);
            }
        }

        /// <summary>Rewrites one scripted call's arguments, for a tool call that needs a stored id.</summary>
        public void Replace(string toolCallId, string arguments) {
            lock (_responses) {
                for (int index = 0; index < _responses.Count; index++) {
                    _responses[index] = [.. _responses[index].Select(@event => @event is ToolCallDelta call && call.Id == toolCallId
                        ? new ToolCallDelta(call.Index, call.Id, call.Name, arguments)
                        : @event)];
                }
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            lock (_systemPrompts) {
                _systemPrompts.Add(request.Messages[0].Content);
            }

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
