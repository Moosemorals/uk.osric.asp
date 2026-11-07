// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Net;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class EditorEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly PageMetrics _metrics = new();
    private readonly TestAntiforgery _antiforgery = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [TestCase("plain text")]
    [TestCase("&amp; <tag> \"quoted\"")]
    [TestCase("</textarea><script>alert('source')</script>")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task GetEncodesStoredSourceAsync(string source) {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), source);

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.Contain($"<textarea name=\"template\" id=\"template\" rows=\"10\" cols=\"50\" wrap=\"off\" spellcheck=\"false\">\n{WebUtility.HtmlEncode(source)}</textarea>"));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/page.cs"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task GetDoesNotCompileThePageAsync() {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "uncompiled");
        int compiles = _compiler.CompileCount;

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("spellcheck=\"false\">\n</textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathReadsTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetEncodesPathAndConsumesTheSessionMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        TestSession session = Session(context);
        session.SetString(EditorEndpoint.MessageKey, "<script>saved</script>");

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing /&lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(session.TryGetValue(EditorEndpoint.MessageKey, out _), Is.False);
    }

    [Test]
    public async Task GetWithoutMessageSetsNoCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [Test]
    public async Task GetRendersAnAntiforgeryTokenAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("value=\"request-token\""));
        Assert.That(_antiforgery.TokenRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetDoesNotCacheTheSourceAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        // The antiforgery system owns the cache headers on a view that carries its field.
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache, no-store"));
    }

    [TestCase("?cmd=")]
    [TestCase("?cmd=unknown")]
    [TestCase("?cmd=delete&cmd=scrub")]
    public async Task InvalidCommandReturnsBadRequestWithoutReadingOrWritingAsync(string query) {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString(query);
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task CanonicalRedirectDoesNotCacheTheEditorUrlAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello", "GET", "/edit/Hello");
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task ListingUsesOrdinalHeadSummariesAndSeparatesDeletedPagesAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");
        FakePageStore storage = new();
        storage["/zeta"] = new PageTemplate(PagePath.Normalize("/zeta"), "not loaded");
        storage["/a&b"] = new PageTemplate(PagePath.Normalize("/a&b"), "not loaded");
        storage["/gone"] = new PageTemplate(PagePath.Normalize("/gone"), "retained");
        await storage.DeletePageAsync(PagePath.Normalize("/gone"));

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(body.IndexOf("/a&amp;b", StringComparison.Ordinal), Is.LessThan(body.IndexOf("/zeta", StringComparison.Ordinal)));
        Assert.That(body, Does.Contain("Deleted pages"));
        Assert.That(body, Does.Contain("/gone (history retained)"));
        Assert.That(body, Does.Not.Contain("href=\"/gone\""));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache, no-store"));
    }

    [Test]
    public async Task ListingGroupsSeededPagesUnderTheirOwnHeadingAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");
        FakePageStore storage = new();
        storage["/page"] = new PageTemplate(PagePath.Normalize("/page"), "not loaded");
        storage["/editor.css"] = new PageTemplate(PagePath.Normalize("/editor.css"), "not loaded");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        int live = body.IndexOf("<h2>Live pages</h2>", StringComparison.Ordinal);
        int page = body.IndexOf("<li><a href=\"/page\">", StringComparison.Ordinal);
        int seeded = body.IndexOf("<h2>Seeded pages</h2>", StringComparison.Ordinal);
        int seed = body.IndexOf("<li><a href=\"/editor.css\">", StringComparison.Ordinal);
        Assert.That(live, Is.GreaterThanOrEqualTo(0));
        Assert.That(page, Is.GreaterThan(live));
        Assert.That(seeded, Is.GreaterThan(page));
        Assert.That(seed, Is.GreaterThan(seeded));
    }

    [Test]
    public async Task ListingSaysWhenNeitherLiveNorSeededPagesExistAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<h2>Live pages</h2><p>No live pages.</p>"));
        Assert.That(body, Does.Contain("<h2>Seeded pages</h2><p>No seeded pages.</p>"));
    }

    [Test]
    public async Task ListingOffersSeedRestoreDisabledOnlyWhereThePageMatchesAsync() {
        PagePath edited = PagePath.Normalize("/assistant.css");
        PagePath matching = PagePath.Normalize("/editor.css");
        PagePath plain = PagePath.Normalize("/plain");
        FakePageStore storage = new();
        storage[edited.Key] = new PageTemplate(edited, "/* edited */");
        storage[matching.Key] = new PageTemplate(matching, (await PageAssetSeeder.ReadSeedTemplateAsync(matching))!);
        storage[plain.Key] = new PageTemplate(plain, "not a seed");
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<form method=\"post\" action=\"/edit/assistant.css?cmd=restore\">"));
        Assert.That(body, Does.Contain("<button type=\"submit\" aria-label=\"Restore /assistant.css from its seed\">Restore from seed</button>"));
        Assert.That(body, Does.Contain("<button type=\"submit\" aria-label=\"Restore /editor.css from its seed\" disabled>Restore from seed</button>"));
        Assert.That(body, Does.Not.Contain("aria-label=\"Restore /plain from its seed\""));
    }

    [Test]
    public async Task ListingOffersRestoreForADeletedSeedAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, "/* edited */");
        await storage.DeletePageAsync(path);
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<form method=\"post\" action=\"/edit/assistant.css?cmd=restore\">"));
        Assert.That(body, Does.Contain("<button type=\"submit\" aria-label=\"Restore /assistant.css from its seed\">Restore from seed</button>"));
    }

    [Test]
    public async Task RestoreWritesTheSeedAndReturnsToTheListingAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(path))!;
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, "/* edited */");
        DefaultHttpContext context = CreateRestoreContext("assistant.css");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit"));
        Assert.That(storage[path.Key]?.Template, Is.EqualTo(seed));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey), Is.EqualTo("Restored /assistant.css from its seed."));
    }

    [Test]
    public async Task RestoreLeavesASeedThatAlreadyMatchesAloneAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(path))!;
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, seed);
        int writes = storage.WriteCount;
        DefaultHttpContext context = CreateRestoreContext("assistant.css");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey), Is.EqualTo("/assistant.css already matched its seed."));
    }

    [Test]
    public async Task RestoreRejectsAPageThatHasNoSeedAsync() {
        DefaultHttpContext context = CreateRestoreContext("plain");
        FakePageStore storage = new();
        storage["/plain"] = new PageTemplate(PagePath.Normalize("/plain"), "not a seed");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task NewPageNormalizesAndRedirectsWithoutSavingAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/edit");
        context.Request.QueryString = new QueryString("?cmd=new");
        SetField(context, "path", new StringValues("Hello//World"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello/world"));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [TestCase("", 1)]
    [TestCase("only line", 1)]
    [TestCase("a\nb", 2)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\rb", 2)]
    public async Task GetRendersOneGutterNumberPerSourceLineAsync(string source, int lines) {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), source);
        string expected = "<div class=\"gutter\" id=\"gutter\" aria-hidden=\"true\">"
            + string.Concat(Enumerable.Range(1, lines).Select(number => $"<span id=\"L{number}\">{number}</span>"))
            + "</div>";

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain(expected));
    }

    [Test]
    public async Task GetRendersTheEditorShellAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<body class=\"editor-page\">"));
        Assert.That(body, Does.Contain("<link rel=\"stylesheet\" href=\"/editor.css\">"));
        Assert.That(body, Does.Contain("<form method=\"post\" action=\"?\" class=\"editor-form\">"));
        Assert.That(body, Does.Contain("<script type=\"module\" src=\"/editor.js\"></script>"));
    }

    [Test]
    public async Task GetPutsNavigationAndActionsInOneMenuAsync() {
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "line one");
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain(string.Concat(
            "<nav class=\"editor-menu\" aria-label=\"Editor navigation\">",
            "<a href=\"/edit\">Page list</a> ",
            "<a href=\"/page.cs\">View file</a> ",
            "<a href=\"/edit/page.cs?cmd=history\">History</a> ",
            "<a href=\"/edit/page.cs?cmd=delete\">Delete</a></nav>")));
        Assert.That(body, Does.Not.Contain("?cmd=scrub"));
    }

    [Test]
    public async Task GetOffersOnlyHistoryAndScrubInTheMenuOfADeletedPageAsync() {
        FakePageStore storage = new();
        storage["/gone"] = new PageTemplate(PagePath.Normalize("/gone"), "retained");
        await storage.DeletePageAsync(PagePath.Normalize("/gone"));
        DefaultHttpContext context = CreateContext(path: "gone");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain(
            "<a href=\"/edit/gone?cmd=history\">History</a>"
            + " <a href=\"/edit/gone?cmd=scrub\">Scrub retained history</a></nav>"));
        Assert.That(body, Does.Not.Contain("?cmd=delete"));
    }

    [Test]
    public async Task GetLinksAPositionedDiagnosticToItsGutterNumberAsync() {
        const string source = "line one\nline two";
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), source, new PageImage(
            TemplateFingerprint.Of(source), "1:build", null, null,
            [new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(2, 5), "CS0103: missing")]));
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<a href=\"#L2\">line 2, column 5: error CS0103: missing</a>"));
        Assert.That(body, Does.Contain("<span id=\"L2\">2</span>"));
    }

    [Test]
    public async Task RevisionViewTakesTheStylesheetWithoutTheEditorLayoutAsync() {
        PagePath path = PagePath.Normalize("/page.cs");
        FakePageStore storage = new();
        PageTemplate page = new(path, "saved source");
        await storage.SavePageAsync(page);

        DefaultHttpContext revision = CreateContext();
        revision.Request.QueryString = new QueryString($"?cmd=revision&hash={page.Hash}");
        await EditorEndpoint.HandleAsync(revision, CreatePages(storage), Reserved, _antiforgery);

        string revisionBody = await TestHttp.ReadBodyAsync(revision);
        Assert.That(revisionBody, Does.Contain("<body>"));
        Assert.That(revisionBody, Does.Contain("<link rel=\"stylesheet\" href=\"/editor.css\">"));
        Assert.That(revisionBody, Does.Contain("<pre class=\"source\" tabindex=\"0\">saved source</pre>"));
    }

    [Test]
    public async Task GeneratedCommandIsNoLongerAnEditorCommandAsync() {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString("?cmd=generated");

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task HistoryListsRevisionsAndShowsSelectedSavedSourceWithoutCompilingAsync() {
        PagePath path = PagePath.Normalize("/page.cs");
        FakePageStore storage = new();
        PageTemplate first = new(path, "first <source>");
        PageTemplate second = new(path, "second source");
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        DefaultHttpContext history = CreateContext();
        history.Request.QueryString = new QueryString("?cmd=history");
        await EditorEndpoint.HandleAsync(history, CreatePages(storage), Reserved, _antiforgery);

        string historyBody = await TestHttp.ReadBodyAsync(history);
        Assert.That(history.Response.StatusCode, Is.EqualTo(200));
        Assert.That(historyBody, Does.Contain(first.Hash));
        Assert.That(historyBody, Does.Contain(second.Hash));
        Assert.That(historyBody, Does.Contain("<strong>Current</strong>"));
        Assert.That(historyBody.IndexOf(second.Hash, StringComparison.Ordinal), Is.LessThan(historyBody.IndexOf(first.Hash, StringComparison.Ordinal)));
        Assert.That(historyBody, Does.Not.Contain("first &lt;source&gt;"));
        Assert.That(history.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));

        DefaultHttpContext revision = CreateContext();
        revision.Request.QueryString = new QueryString($"?cmd=revision&hash={first.Hash}");
        int compiles = _compiler.CompileCount;
        await EditorEndpoint.HandleAsync(revision, CreatePages(storage), Reserved, _antiforgery);

        string revisionBody = await TestHttp.ReadBodyAsync(revision);
        Assert.That(revision.Response.StatusCode, Is.EqualTo(200));
        Assert.That(revisionBody, Does.Contain("first &lt;source&gt;"));
        Assert.That(revisionBody, Does.Not.Contain("second source"));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.WriteCount, Is.EqualTo(2));
        Assert.That(revision.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task DeleteAndScrubConfirmationsDoNotMutateOnGetAsync() {
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "saved");
        DefaultHttpContext delete = CreateContext();
        delete.Request.QueryString = new QueryString("?cmd=delete");

        await EditorEndpoint.HandleAsync(delete, CreatePages(storage), Reserved, _antiforgery);

        string deleteBody = await TestHttp.ReadBodyAsync(delete);
        Assert.That(deleteBody, Does.Contain("retains its template and revision history"));
        PageHeadSummary? liveHead = await storage.GetHeadAsync(PagePath.Normalize("/page.cs"));
        Assert.That(liveHead?.IsDeleted, Is.False);
        await storage.DeletePageAsync(PagePath.Normalize("/page.cs"));

        DefaultHttpContext scrub = CreateContext();
        scrub.Request.QueryString = new QueryString("?cmd=scrub");
        await EditorEndpoint.HandleAsync(scrub, CreatePages(storage), Reserved, _antiforgery);

        string scrubBody = await TestHttp.ReadBodyAsync(scrub);
        Assert.That(scrubBody, Does.Contain("cannot be undone"));
        Assert.That(scrubBody, Does.Contain("Rotate exposed secrets"));
        Assert.That(scrubBody, Does.Contain("name=\"confirmation\""));
        PageTemplate? revision = await storage.GetRevisionByHashAsync(storage["/page.cs"]!.Hash);
        Assert.That(revision, Is.Not.Null);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ScrubConfirmationAndPostRejectALivePageAsync() {
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "saved");

        DefaultHttpContext confirmation = CreateContext();
        confirmation.Request.QueryString = new QueryString("?cmd=scrub");
        await EditorEndpoint.HandleAsync(confirmation, CreatePages(storage), Reserved, _antiforgery);

        DefaultHttpContext post = CreateContext("POST");
        post.Request.QueryString = new QueryString("?cmd=scrub");
        SetField(post, "confirmation", new StringValues("/page.cs"));
        await EditorEndpoint.HandleAsync(post, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(confirmation.Response.StatusCode, Is.EqualTo(409));
        Assert.That(confirmation.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(confirmation), Does.Contain("Delete the page before scrubbing its history").And.Contain("has to be deleted first"));
        Assert.That(post.Response.StatusCode, Is.EqualTo(409));
        PageHeadSummary? head = await storage.GetHeadAsync(PagePath.Normalize("/page.cs"));
        Assert.That(head?.IsDeleted, Is.False);
        Assert.That(await storage.GetRevisionByHashAsync(storage["/page.cs"]!.Hash), Is.Not.Null);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ScrubRequiresExactCanonicalPathAndDoesNotLeakCleanupErrorsAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Request.QueryString = new QueryString("?cmd=scrub");
        SetField(context, "confirmation", new StringValues("/PAGE.CS"));
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "sensitive-source");
        await storage.DeletePageAsync(PagePath.Normalize("/page.cs"));

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(await storage.GetHeadAsync(PagePath.Normalize("/page.cs")), Is.Not.Null);

        DefaultHttpContext failed = CreateContext("POST");
        failed.Request.QueryString = new QueryString("?cmd=scrub");
        SetField(failed, "confirmation", new StringValues("/page.cs"));
        FakePageStore unavailable = new() { Failure = new InvalidOperationException("secret source leaked") };

        await EditorEndpoint.HandleAsync(failed, CreatePages(unavailable), Reserved, _antiforgery);

        string failureBody = await TestHttp.ReadBodyAsync(failed);
        Assert.That(failed.Response.StatusCode, Is.EqualTo(503));
        Assert.That(failureBody, Does.Not.Contain("secret source leaked"));
        Assert.That(failed.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task PostWithInvalidAntiforgeryWritesNothingAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));
        SetTemplate(context, new StringValues("hijack"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("")]
    [TestCase("hello & <world>\n\u00e9")]
    public async Task PostSavesExactTemplateAndRedirectsAsync(string source) {
        DefaultHttpContext context = CreateContext("POST");
        SetTemplate(context, new StringValues(source));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.EqualTo(1));
        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/page.cs"));
        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo(source));
        Assert.That(storage["/page.cs"]?.Image, Is.Not.Null);
        Assert.That(storage.ReadCount, Is.Zero);
        AssertRedirect(context, "Saved successfully.");
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SaveAndViewRedirectsToTheSavedPageAsync() {
        DefaultHttpContext context = CreateContext("POST");
        SetFields(context, ("template", "Hello."), ("cmd", "save-and-view"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/page.cs"));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey), Is.EqualTo("Saved successfully."));
        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo("Hello."));
    }

    [Test]
    public async Task SaveAndViewStaysOnTheEditorWhenThePageHasErrorsAsync() {
        DefaultHttpContext context = CreateContext("POST");
        SetFields(context, ("template", "<% int value = ; %>"), ("cmd", "save-and-view"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        // The page cannot be rendered, so sending the author there would hide the diagnostics
        // behind the error page they would find instead.
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/page.cs"));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey),
            Is.EqualTo("Saved with errors. The page cannot be shown until the errors are fixed."));
        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo("<% int value = ; %>"));
    }

    [Test]
    public async Task ACommandInBothTheFormAndTheQueryMustAgreeAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Request.QueryString = new QueryString("?cmd=delete");
        SetFields(context, ("template", "Hello."), ("cmd", "save-and-view"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AnUnknownFormCommandIsAnHtmlBadRequestAsync() {
        DefaultHttpContext context = CreateContext("POST");
        SetFields(context, ("template", "Hello."), ("cmd", "publish"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("did not name an editor action"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task PostKeepsTheSourceAndShowsDiagnosticsWhenThePageDoesNotCompileAsync() {
        DefaultHttpContext post = CreateContext("POST");
        SetTemplate(post, new StringValues("<% int value = ; %>"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(post, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo("<% int value = ; %>"));
        Assert.That(storage["/page.cs"]?.Image?.Bytes, Is.Null);
        AssertRedirect(post, "Saved with errors.");

        DefaultHttpContext get = CreateContext();
        await EditorEndpoint.HandleAsync(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body, Does.Contain("class=\"diagnostics\""));
        Assert.That(body, Does.Contain("error CS"));
        Assert.That(body, Does.Contain("diagnostic-error"));
    }

    [Test]
    public async Task DiagnosticsAreShownInPositionOrderAsync() {
        DefaultHttpContext get = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "line one\nline two", new PageImage(
            TemplateFingerprint.Of("line one\nline two"),
            "1:build",
            null,
            null,
            [
                new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(2, 5), "CS0103: later line"),
                new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 9), "CS1002: earlier line"),
            ]));

        await EditorEndpoint.HandleAsync(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body.IndexOf("CS1002", StringComparison.Ordinal), Is.LessThan(body.IndexOf("CS0103", StringComparison.Ordinal)));
    }

    [Test]
    public async Task PostWithAWarningSaysSavedWithWarningsAsync() {
        DefaultHttpContext post = CreateContext("POST");
        SetTemplate(post, new StringValues("<% int unused = 1; %>"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(post, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Image?.Bytes, Is.Not.Null.And.Not.Empty);
        AssertRedirect(post, "Saved with warnings.");

        DefaultHttpContext get = CreateContext();
        await EditorEndpoint.HandleAsync(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body, Does.Contain("warning CS0219"));
        Assert.That(body, Does.Contain("diagnostic-warning"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/")]
    public async Task PostWithoutUsablePathWritesTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext("POST", path);
        SetTemplate(context, new StringValues("replacement"));
        FakePageStore storage = new();
        storage["/"] = new PageTemplate(PagePath.Normalize("/"), "original");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/"));
        Assert.That(storage["/"]?.Template, Is.EqualTo("replacement"));
        AssertRedirect(context, "Saved successfully.");
    }

    [TestCase("/edit/Hello", "", "/edit/hello")]
    [TestCase("/edit/hello/", "", "/edit/hello")]
    [TestCase("/edit/Hello", "?tab=source", "/edit/hello?tab=source")]
    public async Task NonCanonicalEditorUrlRedirectsPermanentlyAsync(string requestPath, string query, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath["/edit/".Length..], "GET", requestPath);
        context.Request.QueryString = new QueryString(query);
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task AKeyWithADelimiterIsNotFoundAsync(string method) {
        DefaultHttpContext context = TestHttp.CreateContext("hello?world", method, "/edit/hello?world");
        SetTemplate(context, new StringValues("source"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("/hello?world not found."));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task PostFromNonCanonicalUrlSavesTheCanonicalKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello/", "POST", "/edit/Hello/");
        SetTemplate(context, new StringValues("source"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/hello"));
        Assert.That(storage["/hello"]?.Template, Is.EqualTo("source"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello"));
    }

    [Test]
    public async Task MissingTemplateDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo("original"));
        AssertRedirect(context, "A template is required.");
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task InvalidTemplateCountDoesNotOverwriteAsync(int count) {
        DefaultHttpContext context = CreateContext("POST");
        SetTemplate(context, new StringValues([.. Enumerable.Repeat("replacement", count)]));
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo("original"));
        AssertRedirect(context, "The template must have exactly one value.");
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task StorageFailurePropagatesWithoutARedirectOrMessageAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetTemplate(context, new StringValues("replacement"));
        InvalidOperationException failure = new("Storage unavailable.");
        FakePageStore storage = new() { Failure = failure };

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery));

        Assert.That(thrown?.Message, Is.EqualTo(failure.Message));

        Assert.That(Session(context).Keys, Is.Empty);
        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task CancellationPropagatesAndStoresNothingAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetTemplate(context, new StringValues("replacement"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakePageStore storage = new();

        await Assert.CatchAsync<OperationCanceledException>(() =>
            EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery));

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(Session(context).Keys, Is.Empty);
        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [TestCase("template=hello+%26+%3Cworld%3E%0A%C3%A9", "hello & <world>\n\u00e9", "Saved successfully.", 1)]
    [TestCase("template=", "", "Saved successfully.", 1)]
    [TestCase("other=value", "original", "A template is required.", 0)]
    [TestCase("template=first&template=second", "original", "The template must have exactly one value.", 0)]
    public async Task FormBodyIsDecodedAndValidatedAsync(string body, string expectedTemplate, string message, int writes) {
        DefaultHttpContext context = CreateContext("POST");
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        FakePageStore storage = new();
        storage["/page.cs"] = new PageTemplate(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Template, Is.EqualTo(expectedTemplate));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    [Test]
    public async Task PostToAReservedKeyIsRefusedAndNothingIsStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("favicon.ico", "POST", "/edit/favicon.ico");
        SetTemplate(context, new StringValues("hijack"));
        FakePageStore storage = new();

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/favicon.ico"], Is.Null);
        AssertRedirect(context, "/favicon.ico is reserved by the site.");
    }

    [TestCase("/space name", "/space%20name")]
    [TestCase("/literal%2fvalue", "/literal%252fvalue")]
    [TestCase("/a&b", "/a%26b")]
    public async Task ListingEncodesUrlPathsSeparatelyFromLabelsAsync(string key, string url) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");
        FakePageStore storage = new();
        storage[key] = new PageTemplate(PagePath.Normalize(key), "source");

        await EditorEndpoint.HandleAsync(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain($"href=\"{url}\""));
        Assert.That(body, Does.Not.Contain("?cmd=scrub"));
    }

    [TestCase("space name", "/edit/space%20name")]
    [TestCase("literal%2fvalue", "/edit/literal%252fvalue")]
    public async Task NewPageRedirectEncodesLiteralPathCharactersAsync(string key, string url) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/edit");
        context.Request.QueryString = new QueryString("?cmd=new");
        SetField(context, "path", new StringValues(key));

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(url));
    }

    [TestCase("space name")]
    [TestCase("literal%2fvalue")]
    public async Task CanonicalDecodedRequestPathDoesNotRedirectAsync(string key) {
        DefaultHttpContext context = CreateContext(path: key);

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain($"href=\"{WebUtility.HtmlEncode("/" + Uri.EscapeDataString(key))}\""));
    }

    [Test]
    public async Task MissingHeadCannotBeScrubbedAsync() {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString("?cmd=scrub");

        await EditorEndpoint.HandleAsync(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Not.Contain("name=\"confirmation\""));
    }

    private PageEditor CreatePages(IPageStore storage) {
        PageBudget budget = new(new RenderOptions(), new CompileOptions());
        PageImageBuilder compilations = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageEditor(storage, compilations, _metrics, NullLogger<PageEditor>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method = "GET", string? path = "page.cs") {
        return TestHttp.CreateContext(path, method, "/edit" + PagePath.Normalize(path).Key);
    }

    private static DefaultHttpContext CreateRestoreContext(string path) {
        DefaultHttpContext context = CreateContext(method: "POST", path);
        context.Request.QueryString = new QueryString("?cmd=restore");
        SetField(context, "__RequestVerificationToken", new StringValues("token"));
        return context;
    }

    private static void SetTemplate(DefaultHttpContext context, StringValues template) {
        SetField(context, "template", template);
    }

    private static void SetFields(DefaultHttpContext context, params (string Name, StringValues Value)[] fields) {
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(fields.ToDictionary(field => field.Name, field => field.Value));
    }

    private static void SetField(DefaultHttpContext context, string name, StringValues value) {
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { [name] = value });
    }

    private static TestSession Session(DefaultHttpContext context) {
        return (TestSession)context.Features.Get<ISessionFeature>()!.Session;
    }

    private static void AssertRedirect(DefaultHttpContext context, string message) {
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(context.Request.Path.ToString()));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey), Is.EqualTo(message));
    }
}
