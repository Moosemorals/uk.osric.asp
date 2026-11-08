// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageToolExecutorTests {

    [Test]
    public async Task EditPageReplacesOnlyTheSelectedLinesAndReturnsDiagnosticsAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/notes"), "first\r\nsecond\r\nthird");
        storage["/notes"] = original;
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new {
            path = "/notes",
            startLine = 2,
            endLine = 2,
            expectedRevisionHash = original.Hash,
            replacement = "changed",
        });

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("first\r\nchanged\r\nthird"));
        using JsonDocument document = JsonDocument.Parse(result.Content);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("revisionHash").GetString(), Is.Not.EqualTo(original.Hash));
    }

    [Test]
    public async Task StaleEditReturnsConflictWithoutWritingAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "original");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "edit_page",
            "{\"path\":\"/notes\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"stale\",\"replacement\":\"changed\"}",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("changed since read_page"));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("original"));
    }

    [Test]
    public async Task ProtectedPathIsRejectedBeforeReadingStorageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/editor.js\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task WriteReturnsCompileDiagnosticsAndARevisionHashAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "write_page", "{\"path\":\"/new\",\"content\":\"<% int value = ; %>\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(storage["/new"]?.Template, Is.EqualTo("<% int value = ; %>"));
        using JsonDocument document = JsonDocument.Parse(result.Content);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("revisionHash").GetString(), Is.Not.Empty);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("diagnostics").GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task DeleteCreatesATombstoneAndKeepsTheRevisionAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "source");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "delete_page", "{\"path\":\"/notes\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(await storage.GetPageAsync(PageHarness.Key("/notes")), Is.Null);
        Assert.That((await storage.ListRevisionsAsync(PageHarness.Key("/notes"))).Count, Is.EqualTo(1));
        Assert.That((await storage.GetHeadAsync(PageHarness.Key("/notes")))?.IsDeleted, Is.True);
    }

    [Test]
    public async Task ReadPageLineNumbersReferToTheStoredTemplateSourceAsync() {
        FakePageStore storage = new();
        storage["/template"] = new PageTemplate(PageHarness.Key("/template"),
            "<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"value\" %>\r\n<% } %>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/template\",\"startLine\":5,\"endLine\":5}", CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(result.Content);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.That(data.GetProperty("startLine").GetInt32(), Is.EqualTo(5));
        Assert.That(data.GetProperty("source").GetString(), Is.EqualTo("<%= \"value\" %>"));
    }

    [Test]
    public async Task ListPagesReturnsLiveKeysUnderThePrefixAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/a/child"] = new PageTemplate(PageHarness.Key("/a/child"), "alpha");
        storage["/ab"] = new PageTemplate(PageHarness.Key("/ab"), "alpha");
        storage["/editor.js"] = new PageTemplate(PageHarness.Key("/editor.js"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "list_pages", "{\"prefix\":\"/a\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Paths(result), Is.EqualTo<string[]>(["/a", "/a/child"]));
    }

    [Test]
    public async Task ListPagesIncludesDeletedPagesOnlyWhenAskedAsync() {
        FakePageStore storage = new();
        storage["/live"] = new PageTemplate(PageHarness.Key("/live"), "alpha");
        storage["/gone"] = new PageTemplate(PageHarness.Key("/gone"), "alpha");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/gone"));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult live = await tools.ExecuteAsync("list_pages", "{}", CancellationToken.None);
        ToolExecutionResult all = await tools.ExecuteAsync(
            "list_pages", "{\"includeDeleted\":true}", CancellationToken.None);

        Assert.That(Paths(live), Is.EqualTo<string[]>(["/live"]));
        Assert.That(Paths(all), Is.EqualTo<string[]>(["/gone", "/live"]));
    }

    [Test]
    public async Task ListPagesRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "list_pages", "{\"prefix\":\"/a?b\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("not an addressable page path"));
    }

    [Test]
    public async Task SearchPagesFindsLiteralMatchesWithLineNumbersAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha\nbeta\nAlpha");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "gamma");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"alpha\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Matches(result), Is.EqualTo<string[]>(["/a:1:alpha", "/a:3:Alpha"]));
    }

    [Test]
    public async Task SearchPagesHonoursCaseSensitivityAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha\nbeta\nAlpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"Alpha\",\"caseSensitive\":true}", CancellationToken.None);

        Assert.That(Matches(result), Is.EqualTo<string[]>(["/a:3:Alpha"]));
    }

    [Test]
    public async Task SearchPagesDoesNotClaimTruncationWhenTheMatchesFitExactlyAsync() {
        FakePageStore storage = new();
        storage["/many"] = new PageTemplate(PageHarness.Key("/many"), string.Join('\n', Enumerable.Repeat("hit", 100)));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"hit\"}", CancellationToken.None);

        Assert.That(Data(result).GetProperty("matches").GetArrayLength(), Is.EqualTo(100));
        Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.False);
    }

    [Test]
    public async Task SearchPagesClaimsTruncationWhenMoreMatchesExistAsync() {
        FakePageStore storage = new();
        storage["/many"] = new PageTemplate(PageHarness.Key("/many"), string.Join('\n', Enumerable.Repeat("hit", 101)));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"hit\"}", CancellationToken.None);

        Assert.That(Data(result).GetProperty("matches").GetArrayLength(), Is.EqualTo(100));
        Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.True);
    }

    [Test]
    public async Task SearchPagesRefusesAnEmptyQueryAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("must not be empty"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task ShellListReturnsLiveKeysUnderThePrefixAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/a/child"] = new PageTemplate(PageHarness.Key("/a/child"), "alpha");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "beta");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/b"));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /a\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Is.EqualTo("/a\n/a/child"));
    }

    [Test]
    public async Task ShellCatReturnsTheStoredSourceAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "<h1>Title</h1>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /notes\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Is.EqualTo("<h1>Title</h1>"));
    }

    [Test]
    public async Task ShellGrepReportsPathLineAndTextAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "Alpha\nbeta");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -i alpha /a\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Is.EqualTo("/a:1: Alpha"));
    }

    [Test]
    public async Task ShellRefusesAnUnsupportedCommandAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"rm /notes\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("not supported"));
    }

    [Test]
    public async Task ReadPageRefusesARangeOutsideThePageAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/notes\",\"startLine\":1,\"endLine\":3}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("outside the page"));
    }

    [Test]
    public async Task ReadPageShortensALongSourceAndSaysSoAsync() {
        FakePageStore storage = new();
        string source = string.Join('\n', Enumerable.Range(1, 400).Select(line => new string('x', 40)));
        storage["/long"] = new PageTemplate(PageHarness.Key("/long"), source);
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/long\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        using JsonDocument document = JsonDocument.Parse(result.Content);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(data.GetProperty("source").GetString(), Does.Contain("source omitted"));
        Assert.That(data.GetProperty("totalLines").GetInt32(), Is.EqualTo(400));
    }

    [Test]
    public async Task EditPageOnAMissingPagePointsAtWritePageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "edit_page",
            "{\"path\":\"/missing\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"none\",\"replacement\":\"x\"}",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("Use write_page"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task EditPageRefusesARangeOutsideThePageAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/notes"), "one");
        storage["/notes"] = original;
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new {
            path = "/notes",
            startLine = 2,
            endLine = 2,
            expectedRevisionHash = original.Hash,
            replacement = "x",
        });

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("outside the page"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task WriteRefusesAPageOverTheSizeLimitAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services, new AssistantOptions {
            MaxToolArgumentChars = 8192,
            MaxToolResultChars = 4096,
            MaxPageChars = 1024,
        });
        string arguments = JsonSerializer.Serialize(new { path = "/big", content = new string('x', 2000) });

        ToolExecutionResult result = await tools.ExecuteAsync("write_page", arguments, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("MaxPageChars"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MissingRequiredArgumentIsRejectedBeforeStorageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("read_page", "{}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("Required tool argument").And.Contain("is missing"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("edit_page", "{\"path\":\"/editor.js\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"x\",\"replacement\":\"y\"}", "Edit refused")]
    [TestCase("write_page", "{\"path\":\"/editor.js\",\"content\":\"x\"}", "Write refused")]
    [TestCase("delete_page", "{\"path\":\"/editor.js\"}", "Delete refused")]
    public async Task WritingToolRefusesAProtectedPathAsync(string tool, string arguments, string summary) {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(tool, arguments, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo(summary));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task SearchPagesRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"x\",\"prefix\":\"/a?b\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Search refused"));
    }

    [Test]
    public async Task SearchPagesSkipsDeletedAndProtectedPagesAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/gone"] = new PageTemplate(PageHarness.Key("/gone"), "alpha");
        storage["/editor.js"] = new PageTemplate(PageHarness.Key("/editor.js"), "alpha");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/gone"));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"alpha\"}", CancellationToken.None);

        Assert.That(Matches(result), Is.EqualTo<string[]>(["/a:1:alpha"]));
    }

    [Test]
    public async Task ShellRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /a?b\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Shell command refused"));
    }

    [Test]
    public async Task ShellRefusesAPathThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /a?b\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Shell path refused"));
    }

    [Test]
    public async Task ShellReportsAMissingPageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /missing\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("was not found"));
    }

    [Test]
    public async Task ShellHeadAndTailReturnTheRequestedEndsAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo\nthree");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult head = await tools.ExecuteAsync(
            "shell", "{\"command\":\"head -n 2 /notes\"}", CancellationToken.None);
        ToolExecutionResult tail = await tools.ExecuteAsync(
            "shell", "{\"command\":\"tail -n 2 /notes\"}", CancellationToken.None);

        Assert.That(Output(head), Is.EqualTo("one\ntwo"));
        Assert.That(Output(tail), Is.EqualTo("two\nthree"));
    }

    [Test]
    public async Task ShellWcCountsTheLinesIncludingTheFinalEmptyOneAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"wc -l /notes\"}", CancellationToken.None);

        Assert.That(Output(result), Is.EqualTo("3"));
    }

    [Test]
    public async Task ShellStatDescribesThePageAsync() {
        FakePageStore storage = new();
        PageTemplate page = new(PageHarness.Key("/notes"), "one");
        storage["/notes"] = page;
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"stat /notes\"}", CancellationToken.None);

        Assert.That(Output(result), Does.Contain("/notes\n3 chars\n").And.Contain(page.Hash));
    }

    [Test]
    public async Task ShellShortensOutputThatWouldNotFitInAToolResultAsync() {
        FakePageStore storage = new();
        storage["/long"] = new PageTemplate(PageHarness.Key("/long"), new string('x', 2000));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services, new AssistantOptions {
            MaxToolArgumentChars = 4096,
            MaxToolResultChars = 1024,
            MaxPageChars = 4096,
        });

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /long\"}", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Does.EndWith("[truncated]"));
        Assert.That(Output(result).Length, Is.LessThanOrEqualTo(1024));
    }

    [Test]
    public async Task ReadPageCountsATrailingLineBreakAsAnEmptyFinalLineAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/notes\"}", CancellationToken.None);

        Assert.That(Data(result).GetProperty("totalLines").GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public async Task EditPageRefusesWhenThePageChangesWhileTheEditIsSavedAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/notes"), "one");
        storage["/notes"] = original;
        storage.ReplaceOnConditionalSave = path => new PageTemplate(path, "changed elsewhere");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new {
            path = "/notes",
            startLine = 1,
            endLine = 1,
            expectedRevisionHash = original.Hash,
            replacement = "edited",
        });

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("changed while the edit was being saved"));
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("changed elsewhere"));
    }

    private static string[] Paths(ToolExecutionResult result) =>
        [.. Data(result).GetProperty("pages").EnumerateArray().Select(page => page.GetProperty("path").GetString()!)];

    private static string[] Matches(ToolExecutionResult result) =>
        [.. Data(result).GetProperty("matches").EnumerateArray()
            .Select(match => $"{match.GetProperty("path").GetString()}:{match.GetProperty("line").GetInt32()}:{match.GetProperty("text").GetString()}")];

    private static string Output(ToolExecutionResult result) =>
        Data(result).GetProperty("output").GetString()!;

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static ServiceProvider Services(PageEditor editor) =>
        new ServiceCollection().AddScoped(_ => editor).BuildServiceProvider();

    private static PageToolExecutor CreateTools(ServiceProvider services, AssistantOptions? options = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), new AssistantOptions { ProtectedPaths = ["/editor.js"] }),
        options ?? new AssistantOptions { MaxToolArgumentChars = 4096, MaxToolResultChars = 4096, MaxPageChars = 4096 });
}
