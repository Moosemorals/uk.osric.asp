// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using System.Security.Claims;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>The seeded `/pages` page: its listing, its editor and its refusals.</summary>
/// <remarks>
/// The page reads through `IPageCatalog` and writes through `IPageEditor`, so a test seeds the read
/// store for a view and asserts the page store for a write: each is the seam that call touches. The
/// two over one real database are proven in the integration suite.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageEditorPageTests {

    private const string EditorPath = "/pages";
    private const string DocumentLibraryPath = "/_lib/document";

    private readonly FakePageStore _storage = new();
    private readonly Dictionary<string, string> _hashes = [];
    private readonly FakePageReadStore _read = new();
    private readonly TestAntiforgery _antiforgery = new();

    [SetUp]
    public async Task StoreTheEditorPageAsync() {
        Store(EditorPath, (await SeedAsync(EditorPath))!);
        Store(DocumentLibraryPath, (await SeedAsync(DocumentLibraryPath))!);
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><title><%= layout.Title %></title><link rel="stylesheet" href="/site.css"><% foreach (string href in layout.Stylesheets) { %><link rel="stylesheet" href="<%= href %>"><% } %><main id="main"><%== layout.Body %></main></body></html>
            """);
    }

    private static Task<string?> SeedAsync(string path) =>
        PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

    [Test]
    public async Task TheListingGroupsLiveSeededAndDeletedPagesAsync() {
        Store("/about", (await SeedAsync("/about"))!);
        Store("/notes", "mine");
        Delete("/notes");

        Rendered rendered = await RenderAsync();

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Live pages").And.Contain("Seeded pages").And.Contain("Deleted pages"));
            Assert.That(rendered.Body, Does.Contain("/notes"), "a tombstone keeps its row");
            Assert.That(rendered.Body, Does.Contain("action=\"/pages?cmd=new\""), "the listing creates a page");
            Assert.That(rendered.Body, Does.Contain("/about"), "a seeded page is listed");
        });
    }

    [Test]
    public async Task TheListingOffersNoDeleteForTheEditorItselfAsync() {
        Rendered rendered = await RenderAsync();

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("/pages?edit=%2Fpages"), "the editor can edit itself");
            Assert.That(rendered.Body, Does.Not.Contain("/pages?edit=%2Fpages&amp;cmd=delete"),
                "the capability would refuse it, so the row offers no such link");
        });
    }

    [Test]
    public async Task AShellFileIsNotOfferedAsAPageAsync() {
        Store("/login", "stored but answered by the shell");

        Rendered rendered = await RenderAsync();

        Assert.That(rendered.Body, Does.Contain("unavailable"));
    }

    [Test]
    public async Task TheEditorShowsTheStoredSourceAndTheHashItWasRenderedFromAsync() {
        Store("/about", "live source of about");

        Rendered rendered = await RenderAsync("?edit=%2Fabout");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("live source of about"), "the textarea carries the source");
            Assert.That(rendered.Body, Does.Contain("name=\"expected\" value=\"" + HashOf("/about") + "\""), "the form names its head");
            Assert.That(rendered.Body, Does.Contain("id=\"gutter\"").And.Contain("id=\"L1\""), "the gutter is drawn");
            Assert.That(rendered.Body, Does.Contain("/editor.js"), "the module is loaded");
            Assert.That(rendered.Context.Response.Headers.ContentSecurityPolicy.ToString(), Does.Contain("script-src 'self'"),
                "the page widens the policy for its own module");
        });
    }

    [Test]
    public async Task ANeverSavedKeyOpensAnEmptyEditorAsync() {
        Rendered rendered = await RenderAsync("?edit=%2Fnew");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Editing /new"));
            Assert.That(rendered.Body, Does.Contain("name=\"expected\" value=\"\""), "a new page has no head to save over");
            Assert.That(rendered.Body, Does.Not.Contain("was deleted"));
        });
    }

    [Test]
    public async Task ASaveStoresItsRevisionAndSaysSoAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout", "POST", Form(("template", "new"), ("expected", HashOf("/about")), ("cmd", "save")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(200));
            Assert.That(rendered.Body, Does.Contain("Saved as revision"), "the answer names the revision");
            Assert.That(_storage["/about"]?.Template, Is.EqualTo("new"));
        });
    }

    [Test]
    public async Task ASaveCreatesAPageThatDidNotExistAsync() {
        Rendered rendered = await RenderAsync("?edit=%2Fbrand-new", "POST",
            Form(("template", "<h1>hello</h1>"), ("expected", ""), ("cmd", "save")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(200));
            Assert.That(rendered.Body, Does.Contain("Saved as revision"));
            Assert.That(_storage["/brand-new"]?.Template, Is.EqualTo("<h1>hello</h1>"));
        });
    }

    [Test]
    public async Task AStaleSaveIsAConflictThatKeepsTheTextAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout", "POST",
            Form(("template", "mine, unsaved"), ("expected", "hash-9-/elsewhere"), ("cmd", "save")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(409));
            Assert.That(rendered.Body, Does.Contain("mine, unsaved"), "the author's text is not lost");
            Assert.That(rendered.Body, Does.Contain("old"), "the source that was stored instead is shown");
            Assert.That(_storage["/about"]?.Template, Is.EqualTo("old"));
        });
    }

    [Test]
    public async Task ASaveWithAnErrorIsStoredAndReportedAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout", "POST",
            Form(("template", "<% var x = ; %>"), ("expected", HashOf("/about")), ("cmd", "save")));

        Assert.Multiple(() => {
            Assert.That(_storage["/about"]?.Template, Is.EqualTo("<% var x = ; %>"), "the edit is never lost");
            Assert.That(rendered.Body, Does.Contain("does not compile"));
            Assert.That(rendered.Body, Does.Contain("class=\"diagnostics\""));
        });
    }

    [Test]
    public async Task SaveAndViewLandsOnThePageAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout", "POST",
            Form(("template", "<h1>fine</h1>"), ("expected", HashOf("/about")), ("cmd", "save-and-view")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(303));
            Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/about"));
        });
    }

    [Test]
    public async Task SaveAndViewStaysInTheEditorWhenThePageCannotCompileAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout", "POST",
            Form(("template", "<% var x = ; %>"), ("expected", HashOf("/about")), ("cmd", "save-and-view")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(200), "a page that cannot render is not where the author is sent");
            Assert.That(rendered.Body, Does.Contain("does not compile"));
        });
    }

    [Test]
    public async Task TheDeleteActionTombstonesThePageAsync() {
        Store("/about", "old");

        Rendered rendered = await RenderAsync("?edit=%2Fabout&cmd=delete", "POST", Form());

        PageHeadSummary? head = await _storage.GetHeadAsync(PagePath.Normalize("/about"));

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Deleted /about; revision history is retained."));
            Assert.That(head?.IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task TheHistoryOffersARestoreOnEveryRevisionButTheCurrentOneAsync() {
        Store("/about", "first");
        string older = HashOf("/about");
        Store("/about", "second");

        Rendered rendered = await RenderAsync("?page=%2Fabout");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Current source"));
            Assert.That(rendered.Body, Does.Contain("rev=" + Uri.EscapeDataString(older) + "&amp;cmd=restore"), "an older revision can be restored");
            Assert.That(rendered.Body, Does.Not.Contain("rev=" + Uri.EscapeDataString(HashOf("/about")) + "&amp;cmd=restore"),
                "the current revision needs no restore link");
        });
    }

    [Test]
    public async Task TheDiffShowsBothRevisionsAsync() {
        Store("/about", "first");
        string older = HashOf("/about");
        Store("/about", "second");

        Rendered rendered = await RenderAsync($"?page=%2Fabout&from={older}&to={HashOf("/about")}");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Changes to /about"));
            Assert.That(rendered.Body, Does.Contain("diff"), "the renderer's markup is written raw");
        });
    }

    [Test]
    public async Task ARevisionOfAnotherPageIsNotARevisionOfThisOneAsync() {
        Store("/about", "about");
        Store("/other", "other source");
        string elsewhere = HashOf("/other");

        Rendered rendered = await RenderAsync($"?page=%2Fabout&rev={elsewhere}");

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(404));
            Assert.That(rendered.Body, Does.Contain("not on this page"));
        });
    }

    [Test]
    public async Task TheInstallConfirmationShowsTheSeedsDifferenceAsync() {
        Store("/about", "mine");

        Rendered rendered = await RenderAsync("?edit=%2Fabout&cmd=install");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Install the seed over /about?"));
            Assert.That(rendered.Body, Does.Contain("name=\"expected\" value=\"" + HashOf("/about") + "\""));
        });
    }

    [Test]
    public async Task AKeyThatIsNotASeedHasNothingToInstallAsync() {
        Store("/notes", "mine");

        Rendered rendered = await RenderAsync("?edit=%2Fnotes&cmd=install");

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(404));
            Assert.That(rendered.Body, Does.Contain("not a seeded page"));
        });
    }

    [Test]
    public async Task TheRestoreActionSavesTheChosenRevisionAsANewOneAsync() {
        Store("/about", "the old source");
        string older = HashOf("/about");
        Store("/about", "mine");

        Rendered rendered = await RenderAsync($"?page=%2Fabout&rev={older}&cmd=restore", "POST",
            Form(("expected", HashOf("/about"))));

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Restored the revision as revision"));
            Assert.That(_storage["/about"]?.Template, Is.EqualTo("the old source"));
        });
    }

    [Test]
    public async Task AShellFileCannotBeEditedAsync() {
        Rendered rendered = await RenderAsync("?edit=%2Flogin");

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(400));
            Assert.That(rendered.Body, Does.Contain("shell file"));
        });
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        Rendered rendered = await RenderAsync(signedIn: false);

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(302));
            Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/login?returnUrl=%2Fpages"));
        });
    }

    [Test]
    public async Task TheCreateFormOpensTheNamedPageAsync() {
        Rendered rendered = await RenderAsync("?cmd=new", "POST", Form(("path", "/Notes/First")));

        Assert.Multiple(() => {
            Assert.That(rendered.Status, Is.EqualTo(303));
            Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/pages?edit=%2Fnotes%2Ffirst"));
        });
    }

    [Test]
    public async Task TheEditorLinksToWhateverKeyItIsStoredAtAsync() {
        const string Desk = "/desk";
        Store(Desk, (await SeedAsync(EditorPath))!);
        Store("/about", "about");

        Rendered rendered = await RenderAsync(
            "?edit=%2Fabout", path: Desk, editor: TestOptions.Editor with { Key = Desk });

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("action=\"/desk?edit=%2Fabout\""), "the form posts to the page itself");
            Assert.That(rendered.Body, Does.Contain("href=\"/desk?page=%2Fabout\""), "the links do too");
            Assert.That(rendered.Body, Does.Not.Contain("/pages?"), "the shipped key is not baked in");
        });
    }

    private static Dictionary<string, string> Form(params (string Name, string Value)[] fields) =>
        fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal);

    // The hash the read store gave the page's newest revision, which is what a form is rendered from.
    private string HashOf(string path) => _hashes[PagePath.Normalize(path).Key];

    private void Store(string path, string template) {
        PagePath key = PagePath.Normalize(path);
        PageKind kind = TemplateSourceGenerator.Generate(template).Kind;
        _storage[key.Key] = new PageTemplate(key, template) { Kind = kind };

        // The catalog has to agree with the store about the head's hash, because the form carries the
        // hash it was rendered from and the save is conditional on it.
        _hashes[key.Key] = _read.Save(key.Key, template, templateKind: kind, hash: _storage[key.Key]!.Hash);
    }

    private void Delete(string path) => _read.Delete(PagePath.Normalize(path).Key);

    private async Task<Rendered> RenderAsync(
        string query = "",
        string method = "GET",
        Dictionary<string, string>? form = null,
        bool signedIn = true,
        string path = EditorPath,
        PageEditorOptions? editor = null) {
        using PageHarness pages = new(_storage, readStore: _read);
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), method, path);
        context.Request.QueryString = new QueryString(query);
        if (signedIn) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        }

        if (form is not null) {
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(form.ToDictionary(
                field => field.Key, field => new StringValues(field.Value)));
        }

        await PageEndpoint.HandleAsync(
            context, pages.Renderer, NullLoggerFactory.Instance, pages.CreateEnvironment(_antiforgery, editor), pages.Editor);

        return new Rendered(context, await TestHttp.ReadBodyAsync(context));
    }

    private sealed record Rendered(DefaultHttpContext Context, string Body) {
        public int Status => Context.Response.StatusCode;
    }
}
