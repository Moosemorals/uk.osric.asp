// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class AssistantMemoryPageTests : IntegrationTest {
    private readonly ScriptedPageClient _provider = new();

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task AMemoryCanBeSeenEditedPinnedAndForgottenThroughThePageAsync() {
        const string ownerName = "page-owner";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        _provider.Script(
            [Call("remember-1", "remember", "{\"kind\":\"preference\",\"content\":\"The owner writes British English.\"}")],
            [new ContentDelta("Noted."), new Finished("stop")],
            [new ContentDelta("Hello again."), new Finished("stop")],
            [new ContentDelta("Nothing to add."), new Finished("stop")]);

        using HttpClient client = await CreateSignedInClientAsync(ownerName);
        string first = await CreateConversationAsync(client);
        await SendAsync(client, first, "Remember that I write British English.");

        string page = await client.GetStringAsync("/assistant/memory");
        Match row = Regex.Match(page, "id=\"memory-(?<id>\\d+)\"");
        Assert.That(row.Success, Is.True, "the page lists the memory the assistant wrote");
        long id = long.Parse(row.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
        string version = Regex.Match(page, "name=\"expectedVersion\" value=\"(?<version>\\d+)\"").Groups["version"].Value;

        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("<h1>Memory</h1>"));
            Assert.That(page, Does.Contain("The owner writes British English."));
            Assert.That(page, Does.Contain("<h2>Preferences</h2>"));
            Assert.That(page, Does.Contain("of 200 memories"));
        });

        // The forget page asks first, shows the memory and says what deleting cannot reach.
        string confirmation = await client.GetStringAsync($"/assistant/memory/{id}/forget");
        Assert.Multiple(() => {
            Assert.That(confirmation, Does.Contain("<h1>Forget memory</h1>"));
            Assert.That(confirmation, Does.Contain("The owner writes British English."));
            Assert.That(confirmation, Does.Contain("does not reach backups"));
        });

        // The save carries the version the page rendered, changes the text and pins it.
        string token = await GetAntiforgeryTokenAsync(client, $"/assistant/memory/{id}/forget");
        using HttpResponseMessage saved = await client.PostAsync($"/assistant/memory/{id}", Form(
            ("__RequestVerificationToken", token),
            ("expectedVersion", version),
            ("content", "The owner writes British English and prefers colour."),
            ("kind", "preference"),
            ("pinned", "on")));
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(saved.Headers.Location!.ToString(), Is.EqualTo("/assistant/memory"));

        // The pin is what puts it in a run that asks nothing about it: the terms of "What do you know?"
        // are all stopwords, so the block can only hold the pinned memory.
        string second = await CreateConversationAsync(client);
        await SendAsync(client, second, "What do you know?");
        Assert.That(_provider.SystemPrompts[^1], Does.Contain("The owner writes British English and prefers colour."));

        string reloaded = await client.GetStringAsync("/assistant/memory");
        Assert.Multiple(() => {
            Assert.That(reloaded, Does.Contain("The owner writes British English and prefers colour."));
            Assert.That(reloaded, Does.Contain("class=\"assistant-memory-pinned\">Pinned"));
        });

        // Forgetting from the page is the tool's delete: the next run is not given it.
        string forgetToken = await GetAntiforgeryTokenAsync(client, "/assistant/memory");
        using HttpResponseMessage forgotten = await client.PostAsync($"/assistant/memory/{id}/forget", Form(
            ("__RequestVerificationToken", forgetToken)));
        Assert.That(forgotten.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        string third = await CreateConversationAsync(client);
        await SendAsync(client, third, "What do you know?");
        string emptied = await client.GetStringAsync("/assistant/memory");
        int remaining = await MemoryCountAsync(ownerName);
        Assert.Multiple(() => {
            Assert.That(_provider.SystemPrompts[^1], Does.Not.Contain("<memory id="));
            Assert.That(emptied, Does.Contain("has not remembered anything yet"));
            Assert.That(remaining, Is.Zero);
        });
    }

    [Test]
    public async Task AnotherAccountsMemoryIsNotFoundAndUnchangedAsync() {
        const string ownerName = "page-owner-two";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        await SeedAdditionalUserAsync("page-stranger", Password);
        _provider.Script(
            [Call("remember-1", "remember", "{\"kind\":\"fact\",\"content\":\"The site runs on Podman.\"}")],
            [new ContentDelta("Noted."), new Finished("stop")]);

        using HttpClient owner = await CreateSignedInClientAsync(ownerName);
        string conversation = await CreateConversationAsync(owner);
        await SendAsync(owner, conversation, "Remember that the site runs on Podman.");
        long id = await MemoryIdAsync(ownerName);

        using HttpClient stranger = await CreateSignedInClientAsync("page-stranger");
        string strangerPage = await stranger.GetStringAsync("/assistant/memory");
        // The stranger's own memory page is empty and so has no form, so the token comes from the
        // conversations page: an antiforgery pair belongs to the session, not to one form.
        string token = await GetAntiforgeryTokenAsync(stranger, "/assistant");

        using HttpResponseMessage confirmation = await stranger.GetAsync($"/assistant/memory/{id}/forget");
        using HttpResponseMessage saved = await stranger.PostAsync($"/assistant/memory/{id}", Form(
            ("__RequestVerificationToken", token),
            ("expectedVersion", "1"),
            ("content", "Hijacked."),
            ("kind", "fact")));
        using HttpResponseMessage forgotten = await stranger.PostAsync($"/assistant/memory/{id}/forget", Form(
            ("__RequestVerificationToken", token)));

        int held = await MemoryCountAsync(ownerName);
        Assert.Multiple(() => {
            Assert.That(strangerPage, Does.Not.Contain("The site runs on Podman."));
            Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(forgotten.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(held, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ThePageShowsTheCountAtTheCapAsync() {
        const string ownerName = "page-full";
        _provider.Reset();
        await SeedAdditionalUserAsync(ownerName, Password);
        await FillMemoryAsync(ownerName, 200);

        using HttpClient client = await CreateSignedInClientAsync(ownerName);

        string page = await client.GetStringAsync("/assistant/memory");

        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("200 of 200 memories, which is the cap"));
            Assert.That(page, Does.Contain("cannot remember anything new"));
        });
    }

    private static ChatStreamEvent Call(string id, string name, string arguments) =>
        new ToolCallDelta(0, id, name, arguments);

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
            IReadOnlyList<MemoryCandidate> held = await provider.GetRequiredService<IMemoryStore>().ListAsync(owner);
            Assert.That(held, Has.Count.EqualTo(1));
            return held[0].Id;
        });

    // The cap is 200 rows, and writing them directly is the one way to reach it without the test
    // spending two hundred runs.
    private Task<int> FillMemoryAsync(string owner, int count) =>
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

    private sealed class ScriptedPageClient : IChatCompletionClient {
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
