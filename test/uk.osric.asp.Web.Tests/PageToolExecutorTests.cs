// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
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

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, Context, CancellationToken.None);

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
            "{\"path\":\"/notes\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"stale\",\"replacement\":\"changed\"}", Context,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("changed since read_page"));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("original"));
    }

    [Test]
    public async Task ALockedPageIsReadableAndSaysSoAsync() {
        FakePageStore storage = new();
        storage["/editor.js"] = new PageTemplate(PageHarness.Key("/editor.js"), "export {};");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/editor.js\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("source").GetString(), Is.EqualTo("export {};"));
            Assert.That(Data(result).GetProperty("kind").GetString(), Is.EqualTo("page"));
            Assert.That(Data(result).GetProperty("mode").GetString(), Is.EqualTo("-r--r--r--"));
            Assert.That(Data(result).GetProperty("revisionHash").GetString(), Is.Not.Null);
        });
    }

    [Test]
    public async Task ReadPageReturnsTheShellRecordInsteadOfSourceAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/login\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("kind").GetString(), Is.EqualTo("shell"));
            Assert.That(Data(result).GetProperty("mode").GetString(), Is.EqualTo("-r--r--r--"));
            Assert.That(Data(result).GetProperty("source").GetString(), Does.StartWith("/login\n"));
            Assert.That(Data(result).GetProperty("source").GetString(), Does.Contain("description:"));
            Assert.That(Data(result).GetProperty("totalLines").GetInt32(), Is.EqualTo(5));
            Assert.That(Data(result).GetProperty("revisionHash").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Data(result).GetProperty("compileStatus").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(storage.ReadCount, Is.Zero, "a shell file is not in the store");
        });
    }

    [Test]
    public async Task ReadPageSlicesTheShellRecordByLineAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/login\",\"startLine\":3,\"endLine\":3}", Context, CancellationToken.None);

        Assert.That(Data(result).GetProperty("source").GetString(), Does.StartWith("methods: "));
    }

    [Test]
    public async Task ReadPageRefusesAKeyBeneathAShellFileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/edit/anything\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("beneath the shell file").And.Contain("/edit"));
            Assert.That(storage.ReadCount, Is.Zero);
        });
    }

    [Test]
    public async Task WriteReturnsCompileDiagnosticsAndARevisionHashAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "write_page", "{\"path\":\"/new\",\"content\":\"<% int value = ; %>\"}", Context, CancellationToken.None);

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
            "delete_page", "{\"path\":\"/notes\"}", Context, CancellationToken.None);

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
            "read_page", "{\"path\":\"/template\",\"startLine\":5,\"endLine\":5}", Context, CancellationToken.None);

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
            "list_pages", "{\"prefix\":\"/a\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(PagePaths(result), Is.EqualTo<string[]>(["/a", "/a/child"]));
    }

    [Test]
    public async Task ListPagesMarksShellFilesReadOnlyAsync() {
        FakePageStore storage = new();
        storage["/blog"] = new PageTemplate(PageHarness.Key("/blog"), "alpha");
        storage["/logout"] = new PageTemplate(PageHarness.Key("/logout"), "shadowed by the route");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("list_pages", "{}", Context, CancellationToken.None);

        Dictionary<string, (string Kind, string Mode, bool Deleted, string? Status)> rows = Data(result)
            .GetProperty("pages").EnumerateArray()
            .ToDictionary(
                page => page.GetProperty("path").GetString()!,
                page => (
                    page.GetProperty("kind").GetString()!,
                    page.GetProperty("mode").GetString()!,
                    page.GetProperty("deleted").GetBoolean(),
                    page.GetProperty("status").GetString()),
                StringComparer.Ordinal);

        Assert.Multiple(() => {
            Assert.That(rows["/login"], Is.EqualTo(("shell", "-r--r--r--", false, (string?)null)));
            Assert.That(rows["/blog"], Is.EqualTo(("page", "-rw-r--r--", false, "Not compiled")));
            Assert.That(rows["/logout"], Is.EqualTo(("shell", "-r--r--r--", false, (string?)null)),
                "the shell file is the entry, whatever the store holds at its key");
            Assert.That(rows.ContainsKey("/edit/anything"), Is.False);
        });
    }

    [Test]
    public async Task RefuseBeforeApprovalRefusesASaveToAShellFileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult? refused = tools.RefuseBeforeApproval(
            "write_page", "{\"path\":\"/login\",\"content\":\"x\"}");

        Assert.Multiple(() => {
            Assert.That(refused, Is.Not.Null);
            Assert.That(refused!.IsSuccess, Is.False);
            Assert.That(refused.Summary, Is.EqualTo("Write refused"));
            Assert.That(refused.Content, Does.Contain("shell file"));
            Assert.That(tools.RefuseBeforeApproval("read_page", "{\"path\":\"/login\"}"), Is.Null,
                "a read changes nothing");
            Assert.That(tools.RefuseBeforeApproval("write_page", "{\"path\":\"/new\",\"content\":\"x\"}"), Is.Null,
                "an allowed save is the owner's to approve");
            Assert.That(tools.RefuseBeforeApproval("write_page", "not json"), Is.Null,
                "bad arguments are the tool's own refusal");
            Assert.That(tools.RefuseBeforeApproval("write_page", "{\"path\":\"/editor.js\",\"content\":\"x\"}"), Is.Not.Null,
                "a locked page is refused before the prompt too");
            Assert.That(storage.WriteCount, Is.Zero);
        });
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

        ToolExecutionResult live = await tools.ExecuteAsync("list_pages", "{}", Context, CancellationToken.None);
        ToolExecutionResult all = await tools.ExecuteAsync(
            "list_pages", "{\"includeDeleted\":true}", Context, CancellationToken.None);

        Assert.That(PagePaths(live), Is.EqualTo<string[]>(["/live"]));
        Assert.That(PagePaths(all), Is.EqualTo<string[]>(["/gone", "/live"]));
    }

    [Test]
    public async Task ListPagesRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "list_pages", "{\"prefix\":\"/a?b\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("not an addressable page path"));
    }

    [Test]
    public async Task ListPagesIncludesEachPagesStateAsync() {
        string template = "Loud.";
        FakePageStore storage = new();
        storage["/clean"] = new PageTemplate(PageHarness.Key("/clean"), "Clean.");
        storage["/loud"] = new PageTemplate(PageHarness.Key("/loud"), template, new PageImage(
            TemplateFingerprint.Of(template), "1:3:elsewhere", [1], [2],
            [new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 1), "CS0219: unused")]));
        storage["/borked"] = new PageTemplate(PageHarness.Key("/borked"), "<% broken", new PageImage(
            TemplateFingerprint.Of("<% broken"), "1:3:elsewhere", null, null,
            [new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 4), "CS1525: broken")]));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("list_pages", "{}", Context, CancellationToken.None);

        Assert.That(States(result), Is.EqualTo(new Dictionary<string, string> {
            ["/borked"] = "Compile failed",
            ["/clean"] = "Not compiled",
            ["/loud"] = "Compiled with warnings",
        }));
    }

    [Test]
    public async Task ListPagesSaysStaleForAPageCompiledByAnotherBuildAsync() {
        FakePageStore storage = new() {
            CurrentRuntimeId = "2:3:nowhere",
            ["/blog"] = new PageTemplate(PageHarness.Key("/blog"), "Loud.", new PageImage(
                TemplateFingerprint.Of("Loud."), "1:3:elsewhere", [1], [2], [])),
        };
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("list_pages", "{}", Context, CancellationToken.None);

        Assert.That(States(result)["/blog"], Is.EqualTo("Stale"));
    }

    [Test]
    public async Task ReadPageStillReportsCompiledWithWarningsAsync() {
        // FakePageStore judges against no build, so a page read from it is described without staleness.
        string template = "<% int unused = 1; %>";
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        storage["/loud"] = new PageTemplate(PageHarness.Key("/loud"), template, pages.Images.Build(
            PageHarness.Key("/loud"), template, PageMetrics.ReasonSave, CancellationToken.None));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/loud\"}", Context, CancellationToken.None);

        Assert.That(Data(result).GetProperty("compileStatus").GetString(), Is.EqualTo("Compiled with warnings"));
    }

    [Test]
    public async Task ASeededAssetIsAnOrdinaryWritablePageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "write_page", "{\"path\":\"/site.css\",\"content\":\"body { color: red; }\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(storage["/site.css"]?.Template, Is.EqualTo("body { color: red; }"),
                "the shipped ProtectedPaths list is empty, so the seeded styling is the assistant's to edit");
            Assert.That(Data(result).GetProperty("diagnostics").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task WritingAPageThatWarnsIsASuccessfulCallAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "write_page", "{\"path\":\"/loud\",\"content\":\"<% int unused = 1; %>\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(storage["/loud"]?.Image?.Bytes, Is.Not.Null);
        Assert.That(Data(result).GetProperty("diagnostics")[0].GetProperty("severity").GetString(),
            Is.EqualTo("Warning"));
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
            "search_pages", "{\"query\":\"alpha\"}", Context, CancellationToken.None);

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
            "search_pages", "{\"query\":\"Alpha\",\"caseSensitive\":true}", Context, CancellationToken.None);

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
            "search_pages", "{\"query\":\"hit\"}", Context, CancellationToken.None);

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
            "search_pages", "{\"query\":\"hit\"}", Context, CancellationToken.None);

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
            "search_pages", "{\"query\":\"\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("must not be empty"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task ShellListGivesTheChildrenOfAPathAndNothingDeeperAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/a/child"] = new PageTemplate(PageHarness.Key("/a/child"), "alpha");
        storage["/a/child/grandchild"] = new PageTemplate(PageHarness.Key("/a/child/grandchild"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult plain = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /a\"}", Context, CancellationToken.None);
        ToolExecutionResult deeper = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls -R /a\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(plain), Is.EqualTo("/a/child"), "ls names what is at the path, as a filesystem does");
            Assert.That(Output(deeper).Split('\n'), Is.EqualTo((string[])["/a/child", "/a/child/grandchild"]));
        });
    }

    [Test]
    public async Task ShellListLongAddsThePermissionAndMarksAFolderAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/a/child"] = new PageTemplate(PageHarness.Key("/a/child"), "alpha");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "beta");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/b"));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult root = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls -l /\"}", Context, CancellationToken.None);
        ToolExecutionResult folder = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls -l /a\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(root).Split('\n'), Does.Contain("drwxr-xr-x /a"), "a key with keys beneath it is a folder");
            Assert.That(Output(root).Split('\n'), Does.Not.Contain("-rw-r--r-- /b"), "a deleted page is not listed");
            Assert.That(Output(folder), Is.EqualTo("-rw-r--r-- /a/child"));
        });
    }

    [Test]
    public async Task ShellFindPrintsTheKeysThatMatchItsTestsAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/a/b/c"] = new PageTemplate(PageHarness.Key("/a/b/c"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult all = await tools.ExecuteAsync(
            "shell", "{\"command\":\"find /a\"}", Context, CancellationToken.None);
        ToolExecutionResult typed = await tools.ExecuteAsync(
            "shell", "{\"command\":\"find /a -type d\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(all).Split('\n'), Is.EqualTo((string[])["/a", "/a/b", "/a/b/c"]));
            Assert.That(Output(typed).Split('\n'), Is.EqualTo((string[])["/a", "/a/b"]), "the folders, and only the folders");
        });
    }

    [Test]
    public async Task ShellListIncludesTheShellFilesReadOnlyAsync() {
        FakePageStore storage = new();
        storage["/blog"] = new PageTemplate(PageHarness.Key("/blog"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult root = await tools.ExecuteAsync("shell", "{\"command\":\"ls -l /\"}", Context, CancellationToken.None);
        ToolExecutionResult route = await tools.ExecuteAsync("shell", "{\"command\":\"ls -l /edit\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(root).Split('\n'), Does.Contain("-r--r--r-- /edit"));
            Assert.That(Output(root).Split('\n'), Does.Contain("-r--r--r-- /login"));
            Assert.That(Output(root).Split('\n'), Does.Contain("-rw-r--r-- /blog"));
            Assert.That(Output(route), Is.EqualTo("-r--r--r-- /edit"), "a route owns the subtree, not a page");
        });
    }

    [Test]
    public async Task ShellCatReadsAShellFileRecordAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /login\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.StartWith("/login\n"));
            Assert.That(Output(result), Does.Contain("description:"));
        });
    }

    [Test]
    public async Task ShellCatReturnsTheStoredSourceAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "<h1>Title</h1>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /notes\"}", Context, CancellationToken.None);

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
            "shell", "{\"command\":\"grep -i alpha /a\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Is.EqualTo("/a:1: Alpha"));
    }

    [Test]
    public async Task ShellCatNumbersTheLinesWhenItsOptionIsGivenAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "<h1>Title</h1>\n<p>Body</p>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat -n /notes\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Is.EqualTo("1\t<h1>Title</h1>\n2\t<p>Body</p>"),
                "the numbers are the physical lines read_page and edit_page count");
        });
    }

    [Test]
    public async Task ShellGrepAcceptsTheLineNumberOptionAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "Alpha\nbeta");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -n -i alpha /a\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Is.EqualTo("/a:1: Alpha"),
                "a match carries its line whether -n is given or not");
        });
    }

    [Test]
    public async Task ShellRefusalPointsAtHelpAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        // The shape of the production failures: an option the command has not got, refused, and the
        // model told where the accepted form is written down.
        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -z notes /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("Run help grep"));
            Assert.That(storage.ReadCount, Is.Zero);
        });
    }

    [Test]
    public async Task ShellGrepReadsOneLevelUnlessItIsToldToDescendAsync() {
        FakePageStore storage = new();
        storage["/top"] = new PageTemplate(PageHarness.Key("/top"), "needle here");
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "needle below");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult flat = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep needle /\"}", Context, CancellationToken.None);
        ToolExecutionResult deep = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -r needle /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(flat), Is.EqualTo("/top:1: needle here"),
                "a search reads the level it was given, as a filesystem search does");
            Assert.That(Output(deep).Split('\n'),
                Is.EqualTo((string[])["/folder/deep:1: needle below", "/top:1: needle here"]));
        });
    }

    [Test]
    public async Task ShellGrepSaysWhenTheMatchIsBelowTheLevelItReadAsync() {
        FakePageStore storage = new();
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "needle below");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep needle /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(result), Does.StartWith("No match at this level."));
            Assert.That(Output(result), Does.Contain("matches below this level"),
                "an empty answer says what it did not read");
        });
    }

    [Test]
    public async Task ShellGrepTakesMoreThanOnePathAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "alpha");
        storage["/c"] = new PageTemplate(PageHarness.Key("/c"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep alpha /a /b\"}", Context, CancellationToken.None);

        Assert.That(Output(result).Split('\n'), Is.EqualTo((string[])["/a:1: alpha", "/b:1: alpha"]));
    }

    [Test]
    public async Task ShellGrepReportsThePatternItCannotReadAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep 'a[bc' /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("character class that is not closed").And.Contain("Run help grep"));
        });
    }

    [Test]
    public async Task ShellGrepSaysWhenItStoppedAtItsLimitAsync() {
        FakePageStore storage = new();
        storage["/many"] = new PageTemplate(PageHarness.Key("/many"),
            string.Join('\n', Enumerable.Repeat("needle", 120)));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep needle /many\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.Contain("100 lines shown"), "a capped answer says it was capped");
            Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ShellListSaysWhenItStoppedAtItsLimitAsync() {
        FakePageStore storage = new();
        for (int index = 0; index < 600; index++) {
            string key = $"/p{index}";
            storage[key] = new PageTemplate(PageHarness.Key(key), "alpha");
        }
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services, new AssistantOptions {
            MaxToolArgumentChars = 4096,
            MaxToolResultChars = 65536,
            MaxPageChars = 4096,
        });

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.Contain("500 entries shown"), "a capped listing says it was capped");
            Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ShellCatSaysWhenThePathIsAFolderAsync() {
        FakePageStore storage = new();
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /folder\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("is a folder, not a page"));
            Assert.That(result.Content, Does.Contain("ls /folder"));
        });
    }

    [Test]
    public async Task ShellListOnAPageNamesThePageAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /notes\"}", Context, CancellationToken.None);

        Assert.That(Output(result), Is.EqualTo("/notes"), "ls on a file names the file");
    }

    [Test]
    public async Task ShellListReversesWhenAskedAsync() {
        FakePageStore storage = new();
        storage["/d/a"] = new PageTemplate(PageHarness.Key("/d/a"), "alpha");
        storage["/d/b"] = new PageTemplate(PageHarness.Key("/d/b"), "alpha");
        storage["/d/c"] = new PageTemplate(PageHarness.Key("/d/c"), "alpha");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult forwards = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls /d\"}", Context, CancellationToken.None);
        ToolExecutionResult backwards = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls -r /d\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(forwards).Split('\n'), Is.EqualTo((string[])["/d/a", "/d/b", "/d/c"]));
            Assert.That(Output(backwards).Split('\n'), Is.EqualTo((string[])["/d/c", "/d/b", "/d/a"]));
        });
    }

    [Test]
    public async Task ShellCatAndStatTakeSeveralKeysAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "two");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult cat = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat /a /b\"}", Context, CancellationToken.None);
        ToolExecutionResult numbered = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat -n /a /b\"}", Context, CancellationToken.None);
        ToolExecutionResult stat = await tools.ExecuteAsync(
            "shell", "{\"command\":\"stat /a /b\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(cat), Is.EqualTo("one\ntwo"));
            Assert.That(Output(numbered), Is.EqualTo("1\tone\n1\ttwo"),
                "each page is numbered from one, because the numbers belong to the page");
            Assert.That(Output(stat).Split('\n'), Does.Contain("/a").And.Contain("/b"));
            Assert.That(Output(stat), Does.Not.Contain("shell file"), "both are pages");
        });
    }

    [Test]
    public async Task ShellHeadTailAndWcDefaultTheWayAShellDoesAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"),
            string.Join('\n', Enumerable.Range(1, 15).Select(line => $"line {line}")));
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult head = await tools.ExecuteAsync(
            "shell", "{\"command\":\"head /notes\"}", Context, CancellationToken.None);
        ToolExecutionResult headThree = await tools.ExecuteAsync(
            "shell", "{\"command\":\"head -n 3 /notes\"}", Context, CancellationToken.None);
        ToolExecutionResult tail = await tools.ExecuteAsync(
            "shell", "{\"command\":\"tail /notes\"}", Context, CancellationToken.None);
        ToolExecutionResult counted = await tools.ExecuteAsync(
            "shell", "{\"command\":\"wc /notes\"}", Context, CancellationToken.None);
        ToolExecutionResult countedLong = await tools.ExecuteAsync(
            "shell", "{\"command\":\"wc -l /notes\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(head).Split('\n'), Has.Length.EqualTo(10), "ten lines when no count is given");
            Assert.That(Output(head).Split('\n')[0], Is.EqualTo("line 1"));
            Assert.That(Output(headThree).Split('\n'), Has.Length.EqualTo(3));
            Assert.That(Output(tail).Split('\n'), Has.Length.EqualTo(10));
            Assert.That(Output(tail).Split('\n')[^1], Is.EqualTo("line 15"));
            Assert.That(Output(counted), Is.EqualTo("15 /notes"), "wc counts lines without being told to, and names the key");
            Assert.That(Output(countedLong), Is.EqualTo("15 /notes"));
        });
    }

    [Test]
    public async Task ShellDiffWritesTheNormalFormatByDefaultAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one\ntwo\nthree");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "one\nTWO\nthree");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff /a /b\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Is.EqualTo("2c2\n< two\n---\n> TWO"),
                "the normal format, without the trailing line break a diff file carries");
            Assert.That(Data(result).GetProperty("identical").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task ShellDiffWritesAUnifiedDiffWhenAskedAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one\ntwo\nthree");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "one\nTWO\nthree");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult stock = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff -u /a /b\"}", Context, CancellationToken.None);
        ToolExecutionResult none = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff -U 0 /a /b\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(stock), Does.StartWith("--- /a\n+++ /b\n@@ -1,3 +1,3 @@\n"));
            Assert.That(Output(stock), Does.Contain("-two\n+TWO"));
            Assert.That(Output(none), Does.Contain("@@ -2,1 +2,1 @@"), "-U 0 keeps no context");
        });
    }

    [Test]
    public async Task ShellDiffIgnoresWhitespaceWhenAskedAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one   \ntwo\t\nthree");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "one\ntwo\nthree");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult plain = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff /a /b\"}", Context, CancellationToken.None);
        ToolExecutionResult ignored = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff -b /a /b\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(plain), Is.Not.Empty, "whitespace is a difference by default");
            Assert.That(Output(ignored), Is.Empty, "and -b ignores it, so diff writes nothing");
            Assert.That(Data(ignored).GetProperty("identical").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ShellDiffOfTwoSamePagesWritesNothingAndSaysSoAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one\ntwo");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "one\ntwo");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff /a /b\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Is.Empty);
            Assert.That(Data(result).GetProperty("identical").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ShellDiffRefusesAKeyThatIsNotThereAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "one");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult missing = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff /a /missing\"}", Context, CancellationToken.None);
        ToolExecutionResult folder = await tools.ExecuteAsync(
            "shell", "{\"command\":\"diff /a /folder\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(missing.IsSuccess, Is.False);
            Assert.That(missing.Content, Does.Contain("was not found"));
            Assert.That(folder.IsSuccess, Is.False);
            Assert.That(folder.Content, Does.Contain("is a folder, not a page"));
        });
    }

    [Test]
    public async Task ShellGrepNamesTheKeysOrCountsThemLinesWhenAskedAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "needle\nneedle");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "nothing");
        storage["/c"] = new PageTemplate(PageHarness.Key("/c"), "needle");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult names = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -l needle /\"}", Context, CancellationToken.None);
        ToolExecutionResult counts = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep -c needle /\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(Output(names).Split('\n'), Is.EqualTo((string[])["/a", "/c"]),
                "one key per matching page, however many of its lines match");
            Assert.That(Output(counts).Split('\n'), Does.Contain("/a:2").And.Contain("/b:0").And.Contain("/c:1"),
                "a count answers for every page searched, zeros included");
        });
    }

    [Test]
    public async Task ShellHelpListsEveryCommandWithoutReadingAPageAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"help\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.Contain("cat [-n] <key> [key ...]: "));
            Assert.That(Output(result), Does.Contain("grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]: "));
            Assert.That(Output(result), Does.Contain("help [command]: "));
            Assert.That(storage.ReadCount, Is.Zero, "help reads the table, not the store");
        });
    }

    [Test]
    public async Task ShellHelpDescribesOneCommandAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"help grep\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.StartWith("grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]: "));
            Assert.That(Output(result), Does.Not.Contain("cat [-n]"));
        });
    }

    [Test]
    public async Task ShellHelpRefusesAWordItDoesNotAnswerForAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"help rm\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("not supported").And.Contain("Run help"));
        });
    }

    [Test]
    public async Task ShellRefusesAnUnsupportedCommandAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"rm /notes\"}", Context, CancellationToken.None);

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
            "read_page", "{\"path\":\"/notes\",\"startLine\":1,\"endLine\":3}", Context, CancellationToken.None);

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
            "read_page", "{\"path\":\"/long\"}", Context, CancellationToken.None);

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
            "{\"path\":\"/missing\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"none\",\"replacement\":\"x\"}", Context,
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

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, Context, CancellationToken.None);

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

        ToolExecutionResult result = await tools.ExecuteAsync("write_page", arguments, Context, CancellationToken.None);

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

        ToolExecutionResult result = await tools.ExecuteAsync("read_page", "{}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("Required tool argument").And.Contain("is missing"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("edit_page", "{\"path\":\"/editor.js\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"x\",\"replacement\":\"y\"}", "Edit refused")]
    [TestCase("write_page", "{\"path\":\"/editor.js\",\"content\":\"x\"}", "Write refused")]
    [TestCase("delete_page", "{\"path\":\"/editor.js\"}", "Delete refused")]
    public async Task WritingToolRefusesALockedPageAsync(string tool, string arguments, string summary) {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(tool, arguments, Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Summary, Is.EqualTo(summary));
            Assert.That(result.Content, Does.Contain("Not permitted").And.Contain("Assistant:ProtectedPaths"));
            Assert.That(storage.WriteCount, Is.Zero);
        });
    }

    [TestCase("edit_page", "{\"path\":\"/login\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"x\",\"replacement\":\"y\"}", "Edit refused")]
    [TestCase("write_page", "{\"path\":\"/login\",\"content\":\"x\"}", "Write refused")]
    [TestCase("delete_page", "{\"path\":\"/login\"}", "Delete refused")]
    public async Task WritingToolRefusesAShellFileAsync(string tool, string arguments, string summary) {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(tool, arguments, Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Summary, Is.EqualTo(summary));
            Assert.That(result.Content, Does.Contain("Not permitted").And.Contain("shell file"));
            Assert.That(storage.WriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task SearchPagesRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"x\",\"prefix\":\"/a?b\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Search refused"));
    }

    [Test]
    public async Task SearchPagesCoversShellRecordsAndSkipsDeletedPagesAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "alpha");
        storage["/gone"] = new PageTemplate(PageHarness.Key("/gone"), "alpha");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/gone"));
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "search_pages", "{\"query\":\"the owner\"}", Context, CancellationToken.None);

        string[] matches = Matches(result);
        Assert.Multiple(() => {
            Assert.That(matches, Has.Some.StartsWith("/edit:4:access: the owner"),
                "a shell file's record is searchable, and its line numbers are the record's");
            Assert.That(matches, Has.None.StartsWith("/gone:"));
        });
    }

    [Test]
    public async Task ShellGrepSearchesShellRecordsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"grep sign-in\"}", Context, CancellationToken.None);

        Assert.That(Output(result), Does.Contain("/login:5: description:"));
    }

    [Test]
    public async Task ShellRefusesAPrefixThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"ls '/a?b'\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Shell path refused"));
    }

    [Test]
    public async Task ShellRefusesAPathThatIsNotAddressableAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"cat '/a?b'\"}", Context, CancellationToken.None);

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
            "shell", "{\"command\":\"cat /missing\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("No key /missing is stored").And.Contain("ls / lists what is there"));
    }

    [Test]
    public async Task ShellHeadAndTailReturnTheRequestedEndsAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo\nthree");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult head = await tools.ExecuteAsync(
            "shell", "{\"command\":\"head -n 2 /notes\"}", Context, CancellationToken.None);
        ToolExecutionResult tail = await tools.ExecuteAsync(
            "shell", "{\"command\":\"tail -n 2 /notes\"}", Context, CancellationToken.None);

        Assert.That(Output(head), Is.EqualTo("one\ntwo"));
        Assert.That(Output(tail), Is.EqualTo("two\nthree"));
    }

    [Test]
    public async Task ShellWcCountsALineBreakTerminatedLineOnceAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one\ntwo\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"wc -l /notes\"}", Context, CancellationToken.None);

        Assert.That(Output(result), Is.EqualTo("2 /notes"), "a final line break ends the last line, as POSIX counts it");
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
            "shell", "{\"command\":\"stat /notes\"}", Context, CancellationToken.None);

        Assert.That(Output(result), Does.Contain("/notes\n-rw-r--r--\n3 chars\n").And.Contain(page.Hash));
    }

    [Test]
    public async Task ShellStatDescribesAShellFileWithoutARevisionAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "shell", "{\"command\":\"stat /login\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Output(result), Does.StartWith("/login\n-r--r--r--\nshell file\n"));
            Assert.That(Output(result), Does.Not.Contain("chars\n0"));
        });
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
            "shell", "{\"command\":\"cat /long\"}", Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True, result.Content);
        Assert.That(Output(result), Does.Contain("the answer was cut at"));
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
            "read_page", "{\"path\":\"/notes\"}", Context, CancellationToken.None);

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

        ToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, Context, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Content, Does.Contain("changed while the edit was being saved"));
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("changed elsewhere"));
    }

    [Test]
    public void CheckScriptIsAReadOnlyToolWithARequiredPath() {
        ToolDefinition definition = PageToolExecutor.Definitions.Single(item => item.Name == "check_script");
        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);

        Assert.Multiple(() => {
            Assert.That(definition.NeedsApproval, Is.False);
            Assert.That(parameters.RootElement.GetProperty("properties").TryGetProperty("path", out _), Is.True);
            Assert.That(parameters.RootElement.GetProperty("properties").TryGetProperty("content", out _), Is.False);
            Assert.That(parameters.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo<string[]>(["path"]));
        });
    }

    [Test]
    public void CheckScriptTextIsAReadOnlyToolWithRequiredContent() {
        ToolDefinition definition = PageToolExecutor.Definitions.Single(item => item.Name == "check_script_text");
        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);

        Assert.Multiple(() => {
            Assert.That(definition.NeedsApproval, Is.False);
            Assert.That(parameters.RootElement.GetProperty("properties").TryGetProperty("content", out _), Is.True);
            Assert.That(parameters.RootElement.GetProperty("properties").TryGetProperty("path", out _), Is.False);
            Assert.That(parameters.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo<string[]>(["content"]));
        });
    }

    [Test]
    public async Task CheckScriptAcceptsAValidStoredScriptAsync() {
        FakePageStore storage = new();
        storage["/app.js"] = new PageTemplate(PageHarness.Key("/app.js"), "export const answer = 42;\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("check_script", "{\"path\":\"/app.js\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
            Assert.That(Data(result).GetProperty("diagnostics").GetArrayLength(), Is.Zero);
            Assert.That(Data(result).GetProperty("path").GetString(), Is.EqualTo("/app.js"));
            Assert.That(result.Summary, Is.EqualTo("Checked /app.js: valid"));
        });
    }

    [Test]
    public async Task CheckScriptReportsASyntaxErrorAsASuccessfulCallAsync() {
        FakePageStore storage = new();
        storage["/app.js"] = new PageTemplate(PageHarness.Key("/app.js"), "export function go() {\n    export const answer = 42;\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("check_script", "{\"path\":\"/app.js\"}", Context, CancellationToken.None);

        JsonElement diagnostics = Data(result).GetProperty("diagnostics");
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(diagnostics.EnumerateArray().Select(item => item.GetProperty("message").GetString()),
                Has.Some.StartsWith("SCRIPT001: UnexpectedToken").And.Some.EqualTo("SCRIPT002: unclosed '{'."));
            Assert.That(diagnostics[0].GetProperty("line").GetInt32(), Is.EqualTo(1));
            Assert.That(result.Summary, Does.EndWith("problems"));
            Assert.That(storage.WriteCount, Is.Zero);
            Assert.That(storage.ImageWriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task CheckScriptReportsAnUnterminatedTemplateBlockAsync() {
        FakePageStore storage = new();
        storage["/app.js"] = new PageTemplate(PageHarness.Key("/app.js"), "const a = 1;\n<% if (true) {");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("check_script", "{\"path\":\"/app.js\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(Data(result).GetProperty("diagnostics")[0].GetProperty("message").GetString(),
                Does.StartWith("TEMPLATE001:"));
        });
    }

    [Test]
    public async Task CheckScriptChecksMarkupAsJavaScriptAsync() {
        FakePageStore storage = new();
        storage["/page"] = new PageTemplate(PageHarness.Key("/page"), "<h1>Hello</h1>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync("check_script", "{\"path\":\"/page\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task CheckScriptDoesNotConsultTheKeyExtensionAsync() {
        FakePageStore storage = new();
        storage["/script.html"] = new PageTemplate(PageHarness.Key("/script.html"), "export const answer = 42;\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "check_script", "{\"path\":\"/script.html\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CheckScriptRefusesAShellFileBeforeReadingStorageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "check_script", "{\"path\":\"/login\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Summary, Is.EqualTo("Check refused"));
            Assert.That(result.Content, Does.Contain("no page source"));
            Assert.That(storage.ReadCount, Is.Zero);
        });
    }

    [Test]
    public async Task CheckScriptStillChecksALockedPageAsync() {
        FakePageStore storage = new();
        storage["/editor.js"] = new PageTemplate(PageHarness.Key("/editor.js"), "export const answer = 42;\n");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "check_script", "{\"path\":\"/editor.js\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CheckScriptReportsAMissingPageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "check_script", "{\"path\":\"/missing\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("was not found"));
        });
    }

    [Test]
    public async Task CheckScriptTextChecksContentWithoutTouchingTheStoreAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new { content = "export const answer = 42;\n" });

        ToolExecutionResult result = await tools.ExecuteAsync("check_script_text", arguments, Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
            Assert.That(Data(result).GetProperty("diagnostics").GetArrayLength(), Is.Zero);
            Assert.That(Data(result).TryGetProperty("path", out _), Is.False);
            Assert.That(result.Summary, Is.EqualTo("Checked supplied text: valid"));
            Assert.That(storage.ReadCount, Is.Zero);
            Assert.That(storage.WriteCount, Is.Zero);
            Assert.That(storage.ImageWriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task CheckScriptTextReportsAnInvalidScriptAsASuccessfulCallAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new { content = "const a = { x: 1 y: 2 };" });

        ToolExecutionResult result = await tools.ExecuteAsync("check_script_text", arguments, Context, CancellationToken.None);

        JsonElement diagnostics = Data(result).GetProperty("diagnostics");
        JsonElement error = diagnostics.EnumerateArray()
            .Single(item => item.GetProperty("message").GetString()!.StartsWith("SCRIPT001:", StringComparison.Ordinal));
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(error.GetProperty("line").GetInt32(), Is.EqualTo(1));
            Assert.That(error.GetProperty("column").GetInt32(), Is.EqualTo(18));
            Assert.That(storage.WriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task CheckScriptTextChecksTheLiteralTextAroundTemplateBlocksAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string content = "<% var when = DateTime.UtcNow; %>\nexport const label = \"<%= when %>\";\n";
        string arguments = JsonSerializer.Serialize(new { content });

        ToolExecutionResult result = await tools.ExecuteAsync("check_script_text", arguments, Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CheckScriptTextReportsALoneReturnAsIllegalAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        PageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new { content = "return 1;" });

        ToolExecutionResult result = await tools.ExecuteAsync("check_script_text", arguments, Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("valid").GetBoolean(), Is.False);
            Assert.That(Data(result).GetProperty("diagnostics")[0].GetProperty("message").GetString(),
                Does.Contain("IllegalReturn"));
        });
    }

    private static string[] Paths(ToolExecutionResult result) =>
        [.. Data(result).GetProperty("pages").EnumerateArray().Select(page => page.GetProperty("path").GetString()!)];

    private static string[] PagePaths(ToolExecutionResult result) => [.. Data(result)
        .GetProperty("pages").EnumerateArray()
        .Where(page => page.GetProperty("kind").GetString() == "page")
        .Select(page => page.GetProperty("path").GetString()!)];

    private static Dictionary<string, string> States(ToolExecutionResult result) => Data(result)
        .GetProperty("pages").EnumerateArray()
        .Where(page => page.GetProperty("kind").GetString() == "page")
        .ToDictionary(
            page => page.GetProperty("path").GetString()!,
            page => page.GetProperty("status").GetString()!,
            StringComparer.Ordinal);

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

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    private static PageToolExecutor CreateTools(ServiceProvider services, AssistantOptions? options = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), new AssistantOptions { ProtectedPaths = ["/editor.js"] }),
        options ?? new AssistantOptions { MaxToolArgumentChars = 4096, MaxToolResultChars = 4096, MaxPageChars = 4096 },
        new AssistantMetrics(),
        new FakeAssistantFetcher(),
        new DiffRenderer());
}
