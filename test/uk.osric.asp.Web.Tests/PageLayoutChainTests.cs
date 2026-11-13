// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What happens when a page is wrapped in a layout, and when it is not.</summary>
/// <remarks>
/// Every test drives <see cref="PageEndpoint.HandleAsync"/>, because the chain is the endpoint's
/// decision: what the content produced is only known after it ran.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageLayoutChainTests {

    private const string DefaultLayout = "/_layout";

    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly FakePageFetcher _fetcher = new();
    private readonly FakePageReadStore _readStore = new();
    private readonly FakePageStateStore _state = new();
    private readonly FakePageStore _storage = new();
    private readonly TestAntiforgery _antiforgery = new();
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [Test]
    public async Task AnHtmlPageIsWrappedInTheDefaultLayout() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public async Task APageThatClearsItsLayoutIsServedAlone() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null; %><h1>Alone</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Alone</h1>"));
    }

    [Test]
    public async Task APageMayChooseAnotherLayout() {
        Store("/other", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><%== PageDocument.Head(layout.Title) %><p class="other-layout"></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/other";
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("other-layout"));
        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
    }

    [Test]
    public async Task ANonHtmlPageIsNeverWrapped() {
        StoreLayout();
        Store("/page.css", "body { color: red; }");

        Rendered rendered = await RenderAsync("/page.css");

        Assert.That(rendered.Body, Is.EqualTo("body { color: red; }"));
    }

    [Test]
    public async Task ARedirectIsNeverWrapped() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><% ctx.Redirect("/elsewhere"); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/elsewhere"));
        Assert.That(rendered.Body, Is.Empty);
    }

    [Test]
    public async Task TheContentRunsBeforeItsLayout() {
        StoreLayout();
        Store("/page", """
            <% await ctx.Capabilities.GetRequired<IPageStorage>().SetAsync("ran", "content");
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/page"), "ran"), Is.EqualTo("\"content\""));
    }

    [Test]
    public async Task TheLayoutReceivesTheContentAsItsBody() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><p class="content">hello</p>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<p class=\"content\">hello</p>"));
    }

    [Test]
    public async Task ATitleOrHeadValueSetByTheContentReachesTheLayout() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "The Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<title>The Page</title>"));
    }

    [Test]
    public async Task AWrappedPageThatSetsNoTitleIsAnErrorNamingThePage() {
        StoreLayout();
        Store("/untitled", "<% // deliberately no title %>\n<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("/untitled"));
        Assert.That(rendered.Body, Does.Not.Contain("<h1>Forgot</h1>"));
    }

    [Test]
    public async Task AnUntitledPageOffersItsEditorToTheSignedInOwner() {
        StoreLayout();
        Store("/untitled", "<% // deliberately no title %>\n<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled", signedIn: true);

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("href=\"/pages?edit=/untitled\">Edit this page"));
    }

    [Test]
    public async Task AnUntitledPageOffersNoEditorToAnAnonymousVisitor() {
        StoreLayout();
        Store("/untitled", "<% // deliberately no title %>\n<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled");

        Assert.That(rendered.Body, Does.Not.Contain("href=\"/pages?edit=/untitled\""));
    }

    [Test]
    public void ReadingATitleThatWasNeverSetThrows() {
        PageLayoutState layout = new(DefaultLayout);

        Assert.Throws<InvalidOperationException>(() => _ = layout.Title);
    }

    [Test]
    public async Task TheOutermostLayoutWritesTheTitle() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Site — " + layout.Title; %><%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.Title = "Middle — " + layout.Title; %><p class="middle"></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/middle";
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<title>Site — Middle — Page</title>"));
        Assert.That(rendered.Body, Does.Not.Contain("<title>Middle — Page</title>"));
    }

    [Test]
    public async Task EachLayersHeadMarkupIsWrittenOnce() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.AddHead("<meta name=\"outer\" content=\"1\">"); %><%== PageDocument.Head(layout.Title, head: layout.Head) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.AddHead("<meta name=\"middle\" content=\"1\">"); %><p></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/middle";
               layout.Title = "Page";
               layout.AddHead("<meta name=\"page\" content=\"1\">"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(CountOf(rendered.Body, "name=\"outer\""), Is.EqualTo(1));
        Assert.That(CountOf(rendered.Body, "name=\"middle\""), Is.EqualTo(1));
        Assert.That(CountOf(rendered.Body, "name=\"page\""), Is.EqualTo(1));
        Assert.That(rendered.Body.IndexOf("name=\"outer\"", StringComparison.Ordinal),
            Is.LessThan(rendered.Body.IndexOf("name=\"middle\"", StringComparison.Ordinal)));
        Assert.That(rendered.Body.IndexOf("name=\"middle\"", StringComparison.Ordinal),
            Is.LessThan(rendered.Body.IndexOf("name=\"page\"", StringComparison.Ordinal)));
    }

    [Test]
    public async Task ALayersSheetsComeAfterTheSitesAndBeforeThePagesOwn() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.AddStylesheet("/outer.css"); %><%== PageDocument.Head(layout.Title, layout.Stylesheets) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.AddStylesheet("/middle.css"); %><p></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/middle";
               layout.Title = "Page";
               layout.AddStylesheet("/page.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])[
            "/site.css", "/outer.css", "/middle.css", "/page.css",
        ]));
    }

    [Test]
    public async Task APageThatAddsNoSheetAddsNone() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])["/site.css"]));
    }

    [Test]
    public async Task TwoPagesSheetsKeepTheOrderTheyWereAddedIn() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               layout.AddStylesheet("/second.css");
               layout.AddStylesheet("/first.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])[
            "/site.css", "/second.css", "/first.css",
        ]));
    }

    [Test]
    public async Task TheSameSheetFromTwoLayersIsLinkedOnce() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               layout.AddStylesheet("/site.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])["/site.css"]));
    }

    [Test]
    public async Task TheContentOwnsTheStatusTheContentTypeAndTheHeaders() {
        // The layout is the one that tries, so it is the layout that must be ignored.
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.SetStatus(418);
               ctx.SetContentType("text/plain");
               ctx.SetHeader("X-Layout", "1"); %><%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               ctx.SetStatus(202);
               ctx.SetHeader("X-Page", "1"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status202Accepted));
        Assert.That(rendered.Context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(rendered.Context.Response.Headers["X-Page"].ToString(), Is.EqualTo("1"));
        Assert.That(rendered.Context.Response.Headers["X-Layout"], Is.Empty);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task AllowingScriptsInAnyLayerAllowsThemInTheResponse(bool pageAllows, bool layoutAllows) {
        StoreLayout(allowsScripts: layoutAllows);
        Store("/page", $$"""
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               {{(pageAllows ? "ctx.AllowScripts();" : "")}} %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.Headers.ContentSecurityPolicy.ToString(),
            Does.Contain("script-src 'self'"));
    }

    [Test]
    public async Task TheDefaultLayoutFallingOverServesTheContentUnwrapped() {
        // A layout that does not compile is the failure the fallback exists for: a page that throws
        // is a page fault, which the host reports rather than hides.
        Store(DefaultLayout, "<% int broken = ; %>");
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Page</h1>"));
    }

    [Test]
    public async Task AMissingExplicitLayoutIsAnErrorNamingTheKey() {
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/gone";
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("/gone"));
    }

    [Test]
    public async Task ALayoutMayNameAParentAndTheChainNests() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="outer"><%== layout.Body %></div>
            """);
        Store("/inner", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/outer"; %><div class="inner"><%== layout.Body %></div>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/inner";
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<div class=\"outer\">"));
        Assert.That(rendered.Body, Does.Contain("<div class=\"inner\"><h1>Page</h1>"));
    }

    [Test]
    public async Task ACycleStopsAtTheFirstRepeat() {
        Store("/a", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/b"; %><i>a</i><%== layout.Body %>
            """);
        Store("/b", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/a"; %><i>b</i><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = "/a";
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(CountOf(rendered.Body, "<i>a</i>"), Is.EqualTo(1));
        Assert.That(CountOf(rendered.Body, "<i>b</i>"), Is.EqualTo(1));
    }

    [Test]
    public async Task TheChainStopsAtTheDepthLimit() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page", render: new RenderOptions { MaxLayoutDepth = 1 });

        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        // One layout was rendered, and it wrapped the page: the default points at itself, so a
        // second level would have written a second document.
        Assert.That(CountOf(rendered.Body, "<!DOCTYPE html>"), Is.EqualTo(1));
    }

    [Test]
    public async Task OneRequestTakesOneSlotAndOneDeadline() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page", render: new RenderOptions { Slots = 1 });

        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        // A second render under the same one-slot budget would deadlock if the chain did not share
        // it, so reaching the assertion at all is the proof.
        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public async Task TheBodyBudgetIsSharedByTheChain() {
        StoreLayout();
        string content = new('x', 60);
        Store("/page", $$"""
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>
            {{content}}
            """);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RenderAsync("/page", render: new RenderOptions { MaxBodyChars = 100 }));

        Assert.That(thrown.Message, Does.Contain("/_layout"));
    }

    [Test]
    public async Task EachLayerReadsItsOwnStorage() {
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               var storage = ctx.Capabilities.GetRequired<IPageStorage>();
               await storage.SetAsync("last", ctx.Path);
               await storage.SetAsync("title", layout.Title); %><%== PageDocument.Head("Layout") %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               await ctx.Capabilities.GetRequired<IPageStorage>().SetAsync("last", ctx.Path); %><h1>Page</h1>
            """);

        await RenderAsync("/page");

        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/page"), "last"), Is.EqualTo("\"/page\""));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/_layout"), "last"), Is.EqualTo("\"/_layout\""));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/_layout"), "title"), Is.EqualTo("\"Page\""));
    }

    [Test]
    public async Task TheLayoutSeesTheRequestedPathAndItsOwnKey() {
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><span id="layout-path"><%= ctx.Path %></span> <span id="layout-request"><%= ctx.Request.Path %></span>
            <%== PageDocument.Head("Layout") %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page"; %><span id="page-path"><%= ctx.Path %></span> <span id="page-request"><%= ctx.Request.Path %></span>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-path\">/_layout</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-request\">/page</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"page-path\">/page</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"page-request\">/page</span>"));
    }

    [Test]
    public async Task EveryLayerIsOfferedTheCatalogAsync() {
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               System.Collections.Generic.IReadOnlyList<PageEntry> entries =
                   await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(""); %>
            <%== PageDocument.Head(layout.Title) %><span id="layout-count"><%= entries.Count.ToString() %></span><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page";
               System.Collections.Generic.IReadOnlyList<PageEntry> entries =
                   await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(""); %>
            <span id="page-count"><%= entries.Count.ToString() %></span>
            """);
        _readStore.Save("/blog", "blog");

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"page-count\">1</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-count\">1</span>"));
    }

    [Test]
    public async Task EveryContentLayerIsOfferedTheWriterAsync() {
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>
            <span id="page-writer"><%= (ctx.Capabilities.Get<IPageWriter>() is null ? "no" : "yes") %></span>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"page-writer\">yes</span>"));
    }

    [Test]
    public async Task ALayoutIsNotOfferedTheWriterAsync() {
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %>
            <%== PageDocument.Head(layout.Title) %><span id="layout-writer"><%= (ctx.Capabilities.Get<IPageWriter>() is null ? "no" : "yes") %></span><%== layout.Body %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-writer\">no</span>"), "a layout is markup, not a writer");
    }

    [Test]
    public async Task ALayoutThatAsksForTheWriterIsRefusedAsync() {
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               _ = ctx.Capabilities.GetRequired<IPageWriter>(); %>
            <%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page"));

        Assert.That(refused?.Message, Does.Contain(nameof(IPageWriter)));
    }

    [Test]
    public async Task ALayoutMayReadTheStoreAsync() {
        _readStore.Save("/blog", "<h1>From the store</h1>");
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               PageSource source = (await ctx.Capabilities.GetRequired<IPageCatalog>().ReadAsync("/blog"))!; %>
            <%== PageDocument.Head(layout.Title) %><span id="layout-source"><%= source.Source %></span><%== layout.Body %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-source\">&lt;h1&gt;From the store&lt;/h1&gt;</span>"));
    }

    [Test]
    public async Task TheChainSharesOneReadBudgetAsync() {
        _readStore.Save("/blog", new string('x', 8));
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               _ = await ctx.Capabilities.GetRequired<IPageCatalog>().ReadAsync("/blog"); %>
            <%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page";
               _ = await ctx.Capabilities.GetRequired<IPageCatalog>().ReadAsync("/blog"); %><h1>Page</h1>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page", read: new PageReadOptions { MaxSourceCharsPerRender = 12 }));

        Assert.That(refused?.Message, Does.Contain("MaxSourceCharsPerRender"));
    }

    [Test]
    public async Task TheEditorCapabilityIsOfferedOnlyToTheEditorPageAsync() {
        const string Probe = """<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Probe"; %><% ctx.Write(ctx.Capabilities.Get<IPageEditor>() is null ? "absent" : "present"); %>""";
        Store("/pages", Probe);
        Store("/other", Probe);

        Rendered editor = await RenderAsync("/pages", withEditor: true);
        Rendered other = await RenderAsync("/other", withEditor: true);
        Rendered without = await RenderAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(editor.Body, Does.Contain("present"));
            Assert.That(other.Body, Does.Contain("absent"), "another stored page is never offered it");
            Assert.That(without.Body, Does.Contain("absent"), "a host with no editor supplies none");
        });
    }

    [Test]
    public async Task ALayoutIsNeverOfferedTheEditorCapabilityAsync() {
        Store("/pages", """<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Editor"; %>page""");
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Write(ctx.Capabilities.Get<IPageEditor>() is null ? "absent" : "present"); %>
            <%== PageDocument.Head(layout.Title, layout.Stylesheets) %>
            <%== PageDocument.OpenMain() %><%== layout.Body %><%== PageDocument.CloseMain() %><%== PageDocument.End() %>
            """);

        Rendered rendered = await RenderAsync("/pages", withEditor: true);

        Assert.That(rendered.Body, Does.Contain("absent"));
    }

    private void Store(string path, string template) {
        _storage[path] = new PageTemplate(PagePath.Normalize(path), template);
        _state.LivePages.Add(PagePath.Normalize(path).Key);
    }

    private void StoreLayout(bool allowsScripts = false) {
        Store(DefaultLayout, $$"""
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); {{(allowsScripts ? "ctx.AllowScripts();" : "")}} %><%== PageDocument.Head(layout.Title, layout.Stylesheets, head: layout.Head) %>
            <%== PageDocument.OpenMain() %>
            <%== layout.Body %>
            <%== PageDocument.CloseMain() %>
            <%== PageDocument.End() %>
            """);
    }

    private async Task<Rendered> RenderAsync(
        string path,
        RenderOptions? render = null,
        bool signedIn = false,
        string query = "",
        PageReadOptions? read = null,
        bool withEditor = false) {

        RenderOptions options = render ?? new RenderOptions();
        PageSaver saver = CreateSaver(options);
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", path);
        context.Request.QueryString = new QueryString(query);
        if (signedIn) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        }

        PageEnvironment environment = new(
            new ReservedPaths(ReservedPaths.Default),
            new PageEditorOptions(),
            new PageLayoutConfiguration { Default = DefaultLayout },
            options,
            TestContentTypes.Shipped,
            new PageStorageOptions(),
            new PageFetchOptions(),
            read ?? new PageReadOptions(),
            _state,
            _readStore,
            saver,
            _fetcher,
            _metrics,
            _antiforgery,
            new MarkdownRenderer(),
            new DiffRenderer(),
            new PageSessionOptions(),
            new FakePageSessionStore(),
            TestSessions.Cookie);

        PageEditor? editor = withEditor ? new PageEditor(_storage, saver) : null;
        await PageEndpoint.HandleAsync(context, CreateRenderer(options), NullLoggerFactory.Instance, environment, editor);

        return new Rendered(context, await TestHttp.ReadBodyAsync(context));
    }

    private PageSaver CreateSaver(RenderOptions options) {
        PageBudget budget = new(options, new CompileOptions());
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageSaver(new FakePageRevisionStore(_storage), images, _metrics, NullLogger<PageSaver>.Instance);
    }

    private PageRenderer CreateRenderer(RenderOptions options) {
        PageBudget budget = new(options, new CompileOptions());
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageRenderer(
            _storage,
            _compiler,
            images,
            _metrics,
            budget,
            new PageHealth(options.AbandonedLimit),
            NullLogger<PageRenderer>.Instance);
    }

    private static int CountOf(string text, string value) {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) {
            count++;
        }

        return count;
    }

    private static string[] Sheets(string body) =>
        [.. MarkupReader.Parse(body)
            .Elements("link")
            .Where(link => string.Equals(link.Attribute("rel"), "stylesheet", StringComparison.Ordinal))
            .Select(link => link.Attribute("href")!)];

    private sealed record Rendered(DefaultHttpContext Context, string Body);
}
