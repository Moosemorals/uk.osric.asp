// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using System.Security.Claims;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AssistantMemoryPageTests {

    private static DatabaseTemplate? s_template;

    private readonly TestAntiforgery _antiforgery = new();
    private readonly AssistantOptions _options = new() {
        MaxMemoriesPerOwner = 3,
        MaxMemoryChars = 40,
        MaxMemoriesPerRun = 12,
        MaxMemoryCharsPerRun = 4_000,
    };

    private string _databasePath = string.Empty;
    private StorageDbContext _database = null!;
    private SqliteMemoryStore _memories = null!;

    private static MemorySource Source => new(ConversationId: 7, RunId: "run-1");

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("assistant-memory-page");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("assistant-memory-page");
        _database = new StorageDbContext(DatabaseTemplate.Options(_databasePath));
        _memories = new SqliteMemoryStore(_database, _options, TimeProvider.System);
    }

    [TearDown]
    public void CloseDatabase() {
        _database.Dispose();
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task TheListGroupsByKindAndShowsTheCountAsync() {
        await RememberAsync(MemoryKind.Reference, "The deploy script is in the root.");
        await RememberAsync(MemoryKind.Preference, "British English.");
        await RememberAsync(MemoryKind.Fact, "Runs on Podman.");

        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ListAsync(context, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(body, Does.Contain("<h1>Memory</h1>"));
            Assert.That(body, Does.Contain("<h2>Preferences</h2>"));
            Assert.That(body, Does.Contain("<h2>Facts</h2>"));
            Assert.That(body, Does.Contain("<h2>References</h2>"));
            Assert.That(body, Does.Not.Contain("<h2>Feedback</h2>"), "a kind with nothing in it is left out");
            Assert.That(body, Does.Contain("3 of 3 memories, which is the cap"));
            Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">Memory</span>"),
                "the assistant navigation marks the page it is on");
        });
    }

    [Test]
    public async Task TheListShowsAMemoryAsTextAndNeverAsMarkupAsync() {
        await RememberAsync(MemoryKind.Fact, "</textarea><script>");

        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ListAsync(context, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("</textarea><script>"));
            Assert.That(body, Does.Contain("&lt;/textarea&gt;&lt;script&gt;"));
        });
    }

    [Test]
    public async Task TheListSaysWhereAMemoryCameFromAsync() {
        MemoryCandidate kept = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        await RememberAsync(MemoryKind.Fact, "A note from a deleted conversation.");
        _ = await _memories.ClearProvenanceAsync(7);

        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ListAsync(context, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain($"id=\"memory-{kept.Id}\""));
            Assert.That(body, Does.Contain("From a deleted conversation."));
            Assert.That(body, Does.Not.Contain("href=\"/assistant/7\""), "no conversation survives the delete");
        });
    }

    [Test]
    public async Task TheListShowsThePinAndTheUseCountAsync() {
        MemoryCandidate pinned = await RememberAsync(MemoryKind.Preference, "Prefers colour.");
        _ = await _memories.EditAsync("owner", pinned.Id, pinned.Version, MemoryKind.Preference, "Prefers colour.", pinned: true);
        await _memories.TouchAsync("owner", [pinned.Id], conversationId: 7);

        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ListAsync(context, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("class=\"assistant-memory-pinned\">Pinned</span>"));
            Assert.That(body, Does.Contain("used 1 time"));
            Assert.That(body, Does.Contain("value=\"on\" checked"));
        });
    }

    [Test]
    public async Task AnEmptyListSaysSoRatherThanDrawingHeadingsAsync() {
        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ListAsync(context, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("has not remembered anything yet"));
            Assert.That(body, Does.Not.Contain("<h2>"));
            Assert.That(body, Does.Contain("0 of 3 memories"));
        });
    }

    [Test]
    public async Task ASaveChangesTheContentTheKindAndThePinAndRedirectsAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Preference, "Prefers colour.");
        DefaultHttpContext context = Context("owner", "POST");
        Submit(context,
            ("content", "Prefers colour, except in code."),
            ("kind", "feedback"),
            ("pinned", "on"),
            ("expectedVersion", stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        MemoryCandidate? saved = await _memories.GetAsync("owner", stored.Id);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(303));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/assistant/memory"));
            Assert.That(saved!.Content, Is.EqualTo("Prefers colour, except in code."));
            Assert.That(saved.Kind, Is.EqualTo(MemoryKind.Feedback));
            Assert.That(saved.Pinned, Is.True);
            Assert.That(saved.Version, Is.EqualTo(stored.Version + 1));
        });
    }

    [Test]
    public async Task ASaveFromAStalePageIsRefusedWithTheStoredContentAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Preference, "Prefers colour.");
        _ = await _memories.EditAsync("owner", stored.Id, stored.Version, MemoryKind.Preference, "Prefers navy.", pinned: false);
        DefaultHttpContext context = Context("owner", "POST");
        Submit(context,
            ("content", "Prefers green."),
            ("kind", "preference"),
            ("expectedVersion", stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        MemoryCandidate? kept = await _memories.GetAsync("owner", stored.Id);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(409));
            Assert.That(body, Does.Contain("assistant-memory-error"));
            Assert.That(body, Does.Contain("Prefers navy."));
            Assert.That(body, Does.Contain("Prefers navy.</textarea>"), "the row shows what is stored now");
            Assert.That(kept!.Content, Is.EqualTo("Prefers navy."));
        });
    }

    [Test]
    public async Task ASaveThatLooksLikeASecretKeepsTheTextAndSaysWhyAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Reference, "The key is in the vault.");
        DefaultHttpContext context = Context("owner", "POST");
        Submit(context,
            ("content", "api_key=7f3c9a1b2e"),
            ("kind", "reference"),
            ("expectedVersion", stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        string body = await TestHttp.ReadBodyAsync(context);
        MemoryCandidate? kept = await _memories.GetAsync("owner", stored.Id);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(400));
            Assert.That(body, Does.Contain("password or key assignment"));
            Assert.That(body, Does.Contain("api_key=7f3c9a1b2e"), "the owner's text is not thrown away");
            Assert.That(kept!.Content, Is.EqualTo("The key is in the vault."));
        });
    }

    [Test]
    public async Task ASaveOfAnotherOwnersMemoryIsNotFoundAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        DefaultHttpContext context = Context("other", "POST");
        Submit(context, ("content", "Changed."), ("kind", "fact"), ("expectedVersion", "1"));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        MemoryCandidate kept = (await _memories.GetAsync("owner", stored.Id))!;
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(404));
            Assert.That(kept.Content, Is.EqualTo("Runs on Podman."));
        });
    }

    [Test]
    public async Task ASaveWithAnInvalidAntiforgeryFormWritesNothingAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        DefaultHttpContext context = Context("owner", "POST");
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));
        Submit(context, ("content", "Changed."), ("kind", "fact"), ("expectedVersion", "1"));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        MemoryCandidate kept = (await _memories.GetAsync("owner", stored.Id))!;
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(400));
            Assert.That(kept.Content, Is.EqualTo("Runs on Podman."));
        });
    }

    [Test]
    public async Task ASaveWithAnUnknownKindIsRefusedAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        DefaultHttpContext context = Context("owner", "POST");
        Submit(context, ("content", "Changed."), ("kind", "opinion"), ("expectedVersion", "1"));

        await AssistantMemoryEndpoint.SaveAsync(context, stored.Id, _memories, _antiforgery, _options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task TheForgetConfirmationShowsTheMemoryAndDeletesNothingAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        DefaultHttpContext context = Context();

        await AssistantMemoryEndpoint.ForgetConfirmationAsync(context, stored.Id, _memories, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        int kept = await _memories.CountAsync("owner");
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(body, Does.Contain("<h1>Forget memory</h1>"));
            Assert.That(body, Does.Contain("Runs on Podman."));
            Assert.That(body, Does.Contain("does not reach backups"));
            Assert.That(body, Does.Contain($"action=\"/assistant/memory/{stored.Id}/forget\""));
            Assert.That(kept, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ForgettingDeletesTheHeadItsHistoryAndItsIndexRowAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Preference, "Prefers colour.");
        _ = await _memories.EditAsync("owner", stored.Id, stored.Version, MemoryKind.Preference, "Prefers navy.", pinned: false);
        DefaultHttpContext context = Context("owner", "POST");

        await AssistantMemoryEndpoint.ForgetAsync(context, stored.Id, _memories);

        int held = await _memories.CountAsync("owner");
        IReadOnlyList<MemoryCandidate> matched = await _memories.RecallAsync("owner", "\"navy\"*", 10);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(303));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/assistant/memory"));
            Assert.That(held, Is.Zero);
            Assert.That(_database.Set<MemoryEdit>().Count(), Is.Zero);
            Assert.That(matched, Is.Empty);
        });
    }

    [Test]
    public async Task ForgettingAnotherOwnersMemoryIsNotFoundAsync() {
        MemoryCandidate stored = await RememberAsync(MemoryKind.Fact, "Runs on Podman.");
        DefaultHttpContext context = Context("other", "POST");

        await AssistantMemoryEndpoint.ForgetAsync(context, stored.Id, _memories);

        int held = await _memories.CountAsync("owner");
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(404));
            Assert.That(held, Is.EqualTo(1));
        });
    }

    private async Task<MemoryCandidate> RememberAsync(MemoryKind kind, string content) =>
        (await _memories.RememberAsync("owner", kind, content, Source)).Memory!;

    private static DefaultHttpContext Context(string owner = "owner", string method = "GET") {
        DefaultHttpContext context = TestHttp.CreateContext(null, method, AssistantMemoryEndpoint.Path);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, owner)], authenticationType: "test"));
        return context;
    }

    private static void Submit(DefaultHttpContext context, params (string Name, string Value)[] fields) {
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(fields.ToDictionary(
            field => field.Name,
            field => new StringValues(field.Value)));
    }
}
