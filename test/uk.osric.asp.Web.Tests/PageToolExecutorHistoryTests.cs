// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What the assistant's three history tools answer, and what they refuse.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageToolExecutorHistoryTests {

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    [TestCase("list_revisions", ExpectedResult = false)]
    [TestCase("read_revision", ExpectedResult = false)]
    [TestCase("restore_revision", ExpectedResult = true)]
    public bool OnlyTheRestoreIsApprovalGated(string name) =>
        PageToolExecutor.Definitions.Single(item => item.Name == name).NeedsApproval;

    [Test]
    public void TheHistoryToolsAreOfferedInDeclarationOrderAfterDiffPage() {
        string[] names = [.. PageToolExecutor.Definitions.Select(definition => definition.Name)];
        int diff = Array.IndexOf(names, "diff_page");

        Assert.That(names[diff..(diff + 4)], Is.EqualTo<string[]>(["diff_page", "list_revisions", "read_revision", "restore_revision"]));
    }

    [Test]
    public void TheDefinitionsCarryTheDeclaredArguments() {
        ToolDefinition listing = PageToolExecutor.Definitions.Single(item => item.Name == "list_revisions");
        ToolDefinition reading = PageToolExecutor.Definitions.Single(item => item.Name == "read_revision");
        ToolDefinition restoring = PageToolExecutor.Definitions.Single(item => item.Name == "restore_revision");

        Assert.Multiple(() => {
            Assert.That(Required(listing), Is.EqualTo<string[]>(["path"]));
            Assert.That(Properties(listing), Is.EqualTo<string[]>(["path", "limit", "before"]));
            Assert.That(Required(reading), Is.EqualTo<string[]>(["path", "hash"]));
            Assert.That(Properties(reading), Is.EqualTo<string[]>(["path", "hash", "startLine", "endLine"]));
            Assert.That(Required(restoring), Is.EqualTo<string[]>(["path", "hash"]));
        });
    }

    [Test]
    public async Task ListRevisionsAnswersNewestFirstWithTheHeadMarkedAsync() {
        FakePageStore storage = new();
        PageTemplate first = new(PagePath.Normalize("/notes"), "one");
        PageTemplate second = new(PagePath.Normalize("/notes"), "two");
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        ToolExecutionResult result = await RunAsync(storage, "list_revisions", """{"path":"/notes"}""");

        JsonElement data = Data(result);
        JsonElement revisions = data.GetProperty("revisions");
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(revisions.GetArrayLength(), Is.EqualTo(2));
            Assert.That(revisions[0].GetProperty("isCurrent").GetBoolean(), Is.True);
            Assert.That(revisions[0].GetProperty("editorUrl").GetString(), Does.Contain("cmd=revision"));
            Assert.That(data.GetProperty("deleted").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task ListRevisionsListsADeletedPagesHistoryAsync() {
        FakePageStore storage = new();
        PageTemplate only = new(PagePath.Normalize("/notes"), "was here");
        await storage.SavePageAsync(only);
        await storage.DeletePageAsync(only.Path);

        ToolExecutionResult result = await RunAsync(storage, "list_revisions", """{"path":"/notes"}""");

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("deleted").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("revisions").GetArrayLength(), Is.EqualTo(1));
            Assert.That(data.GetProperty("revisions")[0].GetProperty("isCurrent").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task ListRevisionsRefusesAMissingPageAndAShellFileAsync() {
        ToolExecutionResult missing = await RunAsync(new FakePageStore(), "list_revisions", """{"path":"/notes"}""");
        ToolExecutionResult shell = await RunAsync(new FakePageStore(), "list_revisions", """{"path":"/admin/scrub"}""");

        Assert.Multiple(() => {
            Assert.That(missing.IsSuccess, Is.False);
            Assert.That(missing.Content, Does.Contain("was not found"));
            Assert.That(shell.IsSuccess, Is.False);
            Assert.That(shell.Content, Does.Contain("shell file"));
        });
    }

    [Test]
    public async Task ListRevisionsPagesWithLimitAndBeforeAsync() {
        FakePageStore storage = new();
        List<PageTemplate> revisions = [];
        for (int index = 0; index < 5; index++) {
            PageTemplate revision = new(PagePath.Normalize("/notes"), $"body {index}");
            revisions.Add(revision);
            await storage.SavePageAsync(revision);
        }

        ToolExecutionResult firstPage = await RunAsync(storage, "list_revisions", """{"path":"/notes","limit":2}""");
        JsonElement first = Data(firstPage);
        string lastOfFirst = first.GetProperty("revisions")[1].GetProperty("hash").GetString()!;

        ToolExecutionResult secondPage = await RunAsync(
            storage, "list_revisions", $$"""{"path":"/notes","limit":2,"before":"{{lastOfFirst}}"}""");
        JsonElement second = Data(secondPage);

        Assert.Multiple(() => {
            Assert.That(first.GetProperty("revisions").GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(second.GetProperty("revisions").GetArrayLength(), Is.EqualTo(2));
            Assert.That(second.GetProperty("revisions")[0].GetProperty("hash").GetString(), Is.Not.EqualTo(lastOfFirst));
        });
    }

    [Test]
    public async Task ListRevisionsRefusesABadLimitAndABadCursorAsync() {
        FakePageStore storage = new();
        await storage.SavePageAsync(new PageTemplate(PagePath.Normalize("/notes"), "one"));

        ToolExecutionResult zero = await RunAsync(storage, "list_revisions", """{"path":"/notes","limit":0}""");
        ToolExecutionResult tooMany = await RunAsync(storage, "list_revisions", """{"path":"/notes","limit":501}""");
        ToolExecutionResult malformed = await RunAsync(storage, "list_revisions", """{"path":"/notes","before":"nope"}""");
        ToolExecutionResult foreign = await RunAsync(storage, "list_revisions", $$"""{"path":"/notes","before":"{{new string('a', 64)}}"}""");

        Assert.Multiple(() => {
            Assert.That(zero.IsSuccess, Is.False);
            Assert.That(zero.Content, Does.Contain("limit"));
            Assert.That(tooMany.IsSuccess, Is.False);
            Assert.That(malformed.Content, Does.Contain("64 lowercase hexadecimal"));
            Assert.That(foreign.Content, Does.Contain("not a revision of"));
        });
    }

    [Test]
    public async Task ReadRevisionAnswersTheStoredSourceWithOneBasedLinesAsync() {
        FakePageStore storage = new();
        PageTemplate revision = new(PagePath.Normalize("/notes"), "first\nsecond\nthird");
        await storage.SavePageAsync(revision);
        await storage.SavePageAsync(new PageTemplate(PagePath.Normalize("/notes"), "later"));

        ToolExecutionResult result = await RunAsync(
            storage, "read_revision", $$"""{"path":"/notes","hash":"{{revision.Hash}}","startLine":2,"endLine":3}""");

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("source").GetString(), Is.EqualTo("2| second\n3| third"));
            Assert.That(data.GetProperty("startLine").GetInt32(), Is.EqualTo(2));
            Assert.That(data.GetProperty("endLine").GetInt32(), Is.EqualTo(3));
            Assert.That(data.GetProperty("totalLines").GetInt32(), Is.EqualTo(3));
            Assert.That(data.GetProperty("hash").GetString(), Is.EqualTo(revision.Hash));
            Assert.That(data.GetProperty("editorUrl").GetString(), Does.Contain(revision.Hash));
            Assert.That(data.TryGetProperty("isCurrent", out _), Is.False, "the listing answers whether a revision is the head");
        });
    }

    [Test]
    public async Task ReadRevisionRefusesAForeignOrMalformedHashAsync() {
        FakePageStore storage = new();
        PageTemplate mine = new(PagePath.Normalize("/notes"), "mine");
        PageTemplate other = new(PagePath.Normalize("/other"), "other");
        await storage.SavePageAsync(mine);
        await storage.SavePageAsync(other);

        ToolExecutionResult foreign = await RunAsync(storage, "read_revision", $$"""{"path":"/notes","hash":"{{other.Hash}}"}""");
        ToolExecutionResult malformed = await RunAsync(storage, "read_revision", """{"path":"/notes","hash":"NOPE"}""");
        ToolExecutionResult uppercase = await RunAsync(
            storage, "read_revision", $$"""{"path":"/notes","hash":"{{mine.Hash.ToUpperInvariant()}}"}""");

        Assert.Multiple(() => {
            Assert.That(foreign.IsSuccess, Is.False);
            Assert.That(foreign.Content, Does.Contain("not a revision of"));
            Assert.That(malformed.Content, Does.Contain("64 lowercase hexadecimal"));
            Assert.That(uppercase.Content, Does.Contain("64 lowercase hexadecimal"));
        });
    }

    [Test]
    public async Task ReadRevisionWindowsASourceExactlyAsReadPageDoesAsync() {
        FakePageStore storage = new();
        string source = string.Join('\n', Enumerable.Range(0, 400).Select(index => $"line {index}"));
        PageTemplate revision = new(PagePath.Normalize("/notes"), source);
        await storage.SavePageAsync(revision);

        ToolExecutionResult whole = await RunAsync(storage, "read_revision", $$"""{"path":"/notes","hash":"{{revision.Hash}}"}""", SmallBudget());
        ToolExecutionResult live = await RunAsync(storage, "read_page", """{"path":"/notes"}""", SmallBudget());

        JsonElement read = Data(whole).GetProperty("truncated").GetBoolean() ? Data(whole) : default;
        JsonElement page = Data(live);
        Assert.Multiple(() => {
            Assert.That(whole.IsSuccess, Is.True, whole.Content);
            Assert.That(Data(whole).GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(Data(whole).GetProperty("source").GetString(), Is.EqualTo(page.GetProperty("source").GetString()));
            Assert.That(Data(whole).GetProperty("totalLines").GetInt32(), Is.EqualTo(page.GetProperty("totalLines").GetInt32()));
        });
    }

    [Test]
    public async Task RestoreRevisionAppendsTheSourceAndReportsTheNewHashAsync() {
        FakePageStore storage = new();
        PageTemplate first = new(PagePath.Normalize("/notes"), "one");
        PageTemplate second = new(PagePath.Normalize("/notes"), "two");
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        ToolExecutionResult result = await RunAsync(
            storage, "restore_revision", $$"""{"path":"/notes","hash":"{{first.Hash}}"}""");

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(storage[PagePath.Normalize("/notes").Key]?.Template, Is.EqualTo("one"));
            Assert.That(data.GetProperty("restoredFrom").GetString(), Is.EqualTo(first.Hash));
            Assert.That(data.GetProperty("revisionHash").GetString(), Is.Not.EqualTo(first.Hash));
            Assert.That(data.GetProperty("revisionHash").GetString(), Is.EqualTo(storage[PagePath.Normalize("/notes").Key]?.Hash));
        });
    }

    [Test]
    public async Task RestoreRevisionRefusesTheSourceThePageAlreadyHoldsAsync() {
        FakePageStore storage = new();
        PageTemplate older = new(PagePath.Normalize("/notes"), "same");
        await storage.SavePageAsync(older);
        await storage.SavePageAsync(new PageTemplate(PagePath.Normalize("/notes"), "same"));
        int writes = storage.WriteCount;

        ToolExecutionResult result = await RunAsync(
            storage, "restore_revision", $$"""{"path":"/notes","hash":"{{older.Hash}}"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("would change nothing"));
            Assert.That(storage.WriteCount, Is.EqualTo(writes));
        });
    }

    [Test]
    public async Task RestoreRevisionRefusesAHashFromAnotherPageAndAShellFileAsync() {
        FakePageStore storage = new();
        PageTemplate mine = new(PagePath.Normalize("/notes"), "mine");
        PageTemplate other = new(PagePath.Normalize("/other"), "other");
        await storage.SavePageAsync(mine);
        await storage.SavePageAsync(other);

        ToolExecutionResult foreign = await RunAsync(
            storage, "restore_revision", $$"""{"path":"/notes","hash":"{{other.Hash}}"}""");
        ToolExecutionResult shell = await RunAsync(
            storage, "restore_revision", $$"""{"path":"/admin/scrub","hash":"{{mine.Hash}}"}""");

        Assert.Multiple(() => {
            Assert.That(foreign.IsSuccess, Is.False);
            Assert.That(foreign.Content, Does.Contain("not a revision of"));
            Assert.That(shell.IsSuccess, Is.False);
            Assert.That(shell.Content, Does.Contain("Not permitted"));
        });
    }

    [Test]
    public async Task RestoreRevisionBringsADeletedPageBackAsync() {
        FakePageStore storage = new();
        PageTemplate only = new(PagePath.Normalize("/notes"), "retained");
        await storage.SavePageAsync(only);
        await storage.DeletePageAsync(only.Path);

        ToolExecutionResult result = await RunAsync(
            storage, "restore_revision", $$"""{"path":"/notes","hash":"{{only.Hash}}"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That((storage.GetHeadAsync(only.Path).Result)?.IsDeleted, Is.False);
            Assert.That(storage[only.Path.Key]?.Template, Is.EqualTo("retained"));
        });
    }

    [Test]
    public async Task ListPagesPagesWithLimitAndAfterAsync() {
        FakePageStore storage = new();
        foreach (string key in new List<string> { "/p/a", "/p/b", "/p/c", "/p/d" }) {
            await storage.SavePageAsync(new PageTemplate(PagePath.Normalize(key), "body"));
        }

        ToolExecutionResult firstPage = await RunAsync(storage, "list_pages", """{"prefix":"/p","limit":2}""");
        JsonElement first = Data(firstPage);
        string lastOfFirst = first.GetProperty("pages")[1].GetProperty("path").GetString()!;

        ToolExecutionResult secondPage = await RunAsync(storage, "list_pages", $$"""{"prefix":"/p","limit":2,"after":"{{lastOfFirst}}"}""");
        JsonElement second = Data(secondPage);

        Assert.Multiple(() => {
            Assert.That(first.GetProperty("pages").GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(first.GetProperty("pages")[0].GetProperty("path").GetString(), Is.EqualTo("/p/a"));
            Assert.That(second.GetProperty("pages")[0].GetProperty("path").GetString(), Is.EqualTo("/p/c"),
                "the cursor is exclusive and the order is the listing's own");
            Assert.That(second.GetProperty("truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task AListingLimitOutsideTheConfiguredRangeNamesItAsync() {
        AssistantOptions options = TestOptions.Assistant with { DefaultListLimit = 5, MaxListLimit = 10 };

        ToolExecutionResult result = await RunAsync(new FakePageStore(), "list_pages", """{"limit":11}""", options);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("between 1 and 10"));
            Assert.That(result.Content, Does.Contain("5 rows"));
        });
    }

    [Test]
    public async Task ListPagesRefusesABadLimitAsync() {
        ToolExecutionResult result = await RunAsync(new FakePageStore(), "list_pages", """{"limit":0}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("limit"));
        });
    }

    private static AssistantOptions SmallBudget() => TestOptions.Assistant with {
        MaxToolArgumentChars = 65_536,
        MaxToolResultChars = 2_048,
        MaxPageChars = 65_536,
    };

    private static string[] Properties(ToolDefinition definition) {
        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);

        return [.. parameters.RootElement.GetProperty("properties").EnumerateObject().Select(property => property.Name)];
    }

    private static string[] Required(ToolDefinition definition) {
        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);

        return [.. parameters.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()!)];
    }

    private static async Task<ToolExecutionResult> RunAsync(
        FakePageStore storage,
        string tool,
        string arguments,
        AssistantOptions? options = null) {
        using PageHarness pages = new(storage);
        using ServiceProvider services = new ServiceCollection()
            .AddScoped(_ => pages.Editor)
            .AddSingleton(TestOptions.Editor)
            .BuildServiceProvider();

        PageToolExecutor tools = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), TestOptions.Assistant),
            options ?? TestOptions.Assistant with { MaxToolArgumentChars = 65_536, MaxToolResultChars = 65_536, MaxPageChars = 65_536 },
            new AssistantMetrics(),
            new FakeAssistantFetcher());

        return await tools.ExecuteAsync(tool, arguments, Context, CancellationToken.None);
    }

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }
}
