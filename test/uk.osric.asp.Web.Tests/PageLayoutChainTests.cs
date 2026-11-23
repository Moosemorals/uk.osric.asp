// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What happens when a page declares a wrapper, and when it declares none.</summary>
/// <remarks>
/// Every test drives <see cref="PageEndpoint.HandleAsync"/>, because the chain is the endpoint's
/// decision: what the content produced is only known after it ran.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Each test owns its storage - a database file of its own, or a store and harness of its own - so the
// tests here are independent, and waiting for one to finish before starting the next only makes the
// suite slower than the work it is doing.
[Parallelizable(ParallelScope.All)]
public sealed class PageLayoutChainTests {

    private const string DefaultLayout = "/_layout";

    private readonly RecordingCompiler _compiler = TestCompiler.Counting();
    private readonly FakePageFetcher _fetcher = new();
    private readonly FakePageReadStore _readStore = new();
    private readonly FakeStoreValues _state = new();
    private readonly FakeStoreMessages _messages = new();
    private readonly FakeStoreObjects _objects = new();
    private readonly FakePageStore _storage = new();
    private readonly TestAntiforgery _antiforgery = new();
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [Test]
    public async Task APageThatDeclaresTheSiteLayoutIsWrappedInIt() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public async Task APageThatDeclaresNoWrapperIsServedAlone() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Alone</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Alone</h1>"));
    }

    [Test]
    public async Task APageMayDeclareAnotherWrapper() {
        Store("/other", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><title><%= layout.Title ?? "" %></title><p class="other-layout"></p><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/other");
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("other-layout"));
        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
    }

    [Test]
    public async Task AWrapDeclaredNearTheTopSurvivesAGuardsReturn() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               if (ctx.User is null) { ctx.Capabilities.GetRequired<IPageLayout>().Title = "Signed out"; return; } %>
            <h1>Signed in</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(rendered.Body, Does.Contain("<title>Signed out</title>"));
        Assert.That(rendered.Body, Does.Not.Contain("<h1>Signed in</h1>"));
    }

    [Test]
    public async Task AWrapperMayPeekAMessageTheContentLeftAsync() {
        _readStore.Save("/peek-layout", "layout", templateKind: uk.osric.asp.Core.Pages.PageKind.Page);
        Store("/peek-layout", """
            <% PageMessageRead read = await ctx.Stores.Visitor.Messages.PeekAsync(); %>
            <p class="peek"><%= read.Found ? read.Message!.Body : "empty" %></p><%== ctx.Capabilities.GetRequired<IPageLayout>().Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/peek-layout");
               PageMessagePostResult posted = await ctx.Stores.Visitor.Messages.PostAsync("/peek-layout", "queued");
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><p class="post"><%= posted.Refusal.ToString() %></p><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<p class=\"post\">None</p>"));
        Assert.That(rendered.Body, Does.Contain("<p class=\"peek\">queued</p>"));
    }

    [Test]
    public async Task ANonHtmlPageIsNeverWrapped() {
        StoreLayout();
        Store("/page.css", """
            <% ctx.Response.SetContentType("text/css; charset=utf-8");
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %><%== "body { color: red; }" %>
            """);

        Rendered rendered = await RenderAsync("/page.css");

        Assert.That(rendered.Body, Is.EqualTo("body { color: red; }"));
    }

    [Test]
    public async Task ARedirectIsNeverWrapped() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page";
               ctx.Response.Redirect("/elsewhere"); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/elsewhere"));
        Assert.That(rendered.Body, Is.Empty);
    }

    [Test]
    public async Task TheContentRunsBeforeItsWrapper() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               await ctx.Capabilities.GetRequired<IPageStorage>().SetAsync("ran", "content");
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/page"), "ran"), Is.EqualTo("\"content\""));
    }

    [Test]
    public async Task TheWrapperReceivesTheContentAsACapture() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><p class="content">hello</p>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<p class=\"content\">hello</p>"));
    }

    [Test]
    public async Task TheContentTitleAndHeadReachTheWrapperAsAValue() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               layout.Title = "The Page";
               layout.AddHead("<meta name=\"page\" content=\"1\">"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<title>The Page</title>"));
        Assert.That(rendered.Body, Does.Contain("<meta name=\"page\" content=\"1\">"));
    }

    [Test]
    public async Task APageThatSetsNoTitleArrivesAtTheWrapperWithNone() {
        StoreLayout();
        Store("/untitled", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %><h1>Untitled</h1>
            """);

        Rendered rendered = await RenderAsync("/untitled");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(rendered.Body, Does.Contain("<h1>Untitled</h1>"));
        Assert.That(rendered.Body, Does.Contain("<title>Site</title>"), "the wrapper chooses the fallback");
    }

    [Test]
    public void ATitleThatWasNeverSetIsNull() {
        PageLayoutState layout = new();

        Assert.That(layout.Title, Is.Null);
    }

    [Test]
    public void ATitleMayNotBeBlank() {
        PageLayoutState layout = new();

        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => layout.Title = "");
            Assert.Throws<ArgumentException>(() => layout.Title = "   ");
            Assert.Throws<ArgumentNullException>(() => layout.Title = null);
        });
    }

    [Test]
    public async Task TheOutermostWrapperWritesTheTitle() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Site — " + (layout.Content?.Title ?? "none"); %><title><%= layout.Title ?? "" %></title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/outer");
               layout.Title = "Middle — " + (layout.Content?.Title ?? "none"); %><p class="middle"></p><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/middle");
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<title>Site — Middle — Page</title>"));
        Assert.That(rendered.Body, Does.Not.Contain("<title>Middle — Page</title>"));
    }

    [Test]
    public async Task EachWrapperFoldsTheHeadItChooses() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.AddHead("<meta name=\"outer\" content=\"1\">"); %><title><%= layout.Title ?? "" %></title><%== layout.Head %><%== layout.Content?.Head ?? "" %><%== layout.Content?.Body ?? "" %>
            """);
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/outer");
               layout.AddHead("<meta name=\"middle\" content=\"1\">");
               if (layout.Content is { } folded) { layout.AddHead(folded.Head); } %><p></p><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/middle");
               layout.Title = "Page";
               layout.AddHead("<meta name=\"page\" content=\"1\">"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(CountOf(rendered.Body, "name=\"outer\""), Is.EqualTo(1));
            Assert.That(CountOf(rendered.Body, "name=\"middle\""), Is.EqualTo(1));
            Assert.That(CountOf(rendered.Body, "name=\"page\""), Is.EqualTo(1));
            Assert.That(rendered.Body.IndexOf("name=\"outer\"", StringComparison.Ordinal),
                Is.LessThan(rendered.Body.IndexOf("name=\"middle\"", StringComparison.Ordinal)));
            Assert.That(rendered.Body.IndexOf("name=\"middle\"", StringComparison.Ordinal),
                Is.LessThan(rendered.Body.IndexOf("name=\"page\"", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task AWrappersSheetsComeAfterItsOwnAndCarryThePagesOwnLast() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.AddStylesheet("/outer.css"); %><title><%= layout.Title ?? "" %></title><link rel="stylesheet" href="/site.css"><%
               foreach (string href in layout.Stylesheets) { %><link rel="stylesheet" href="<%= href %>"><% }
               if (layout.Content is { } folded) { foreach (string href in folded.Stylesheets) { %><link rel="stylesheet" href="<%= href %>"><% } } %>
            <%== layout.Content?.Body ?? "" %>
            """);
        // A middle wrapper that wants the page's sheets to reach the outermost one adds them to its
        // own slots: the host passes nothing on behind a wrapper's back.
        Store("/middle", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/outer");
               layout.AddStylesheet("/middle.css");
               if (layout.Content is { } folded) {
                   foreach (string href in folded.Stylesheets) { layout.AddStylesheet(href); }
               } %><p></p><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/middle");
               layout.Title = "Page";
               layout.AddStylesheet("/page.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo([
            "/site.css", "/outer.css", "/middle.css", "/page.css",
        ]));
    }

    [Test]
    public async Task APageThatAddsNoSheetAddsNone() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo(["/site.css"]));
    }

    [Test]
    public async Task TwoPagesSheetsKeepTheOrderTheyWereAddedIn() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               layout.Title = "Page";
               layout.AddStylesheet("/second.css");
               layout.AddStylesheet("/first.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo([
            "/site.css", "/second.css", "/first.css",
        ]));
    }

    [Test]
    public async Task ALayersSlotsStayItsOwnUntilTheWrapperFoldsThem() {
        // The wrapper ignores the content's sheet, so the page's sheet is not in the document: the
        // host merges nothing behind the outer page's back.
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><title><%= layout.Title ?? "" %></title><link rel="stylesheet" href="/site.css"><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               layout.Title = "Page";
               layout.AddStylesheet("/page.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo(["/site.css"]));
    }

    [Test]
    public async Task TheContentOwnsTheStatusTheContentTypeAndTheHeaders() {
        // The wrapper is the one that tries, so it is the wrapper that must be ignored.
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Response.SetStatus(418);
               ctx.Response.SetContentType("text/plain");
               ctx.Response.SetHeader("X-Layout", "1"); %><title><%= layout.Title ?? "" %></title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page";
               ctx.Response.SetStatus(202);
               ctx.Response.SetHeader("X-Page", "1"); %><h1>Page</h1>
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
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap();
               layout.Title = "Page";
               {{(pageAllows ? "ctx.Response.AllowScripts();" : "")}} %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.Headers.ContentSecurityPolicy.ToString(),
            Does.Contain("script-src 'self'"));
    }

    [Test]
    public async Task TheDefaultWrapperFallingOverServesTheContentUnwrapped() {
        // A wrapper that does not compile is the failure the fallback exists for: a page that throws
        // is a page fault, which the host reports rather than hides.
        Store(DefaultLayout, "<% int broken = ; %>");
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Page</h1>"));
    }

    [Test]
    public async Task ADefaultWrapperThatCannotBeServedIsSkippedAndTheRestOfTheChainRuns() {
        Store(DefaultLayout, "<% int broken = ; %>");
        Store("/other", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="other"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               composition.Wrap();
               composition.Wrap("/other"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(rendered.Body, Does.Contain("<div class=\"other\"><h1>Page</h1></div>"),
                "the default was skipped and the wrapper the page named still ran");
        });
    }

    [Test]
    public async Task AMissingNamedWrapperIsAnErrorNamingTheKey() {
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/gone");
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("/gone"));
    }

    [Test]
    public async Task AWrapperThatHasNoCodeIsAnErrorNamingTheKey() {
        Store("/plain", "<h1>A document, not a wrapper</h1>");
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/plain");
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("/plain"));
    }

    [Test]
    public async Task AWrapperMayDeclareItsOwnParentAndTheChainNests() {
        Store("/outer", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="outer"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/inner", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/outer"); %><div class="inner"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               composition.Wrap("/inner");
               layout.Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<div class=\"outer\">"));
        Assert.That(rendered.Body, Does.Contain("<div class=\"inner\"><h1>Page</h1>"));
    }

    [Test]
    public async Task TheCallsStackOutwardInTheOrderTheyWereDeclared() {
        Store("/first", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="first"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/second", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="second"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               composition.Wrap("/first");
               composition.Wrap("/second"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Is.EqualTo(
            "<div class=\"second\"><div class=\"first\"><h1>Page</h1></div></div>"));
    }

    [Test]
    public async Task ACycleIsRefused() {
        Store("/a", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/b"); %><i>a</i>
            """);
        Store("/b", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/a"); %><i>b</i>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/a"); %><h1>Page</h1>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page"));

        Assert.That(refused?.Message, Does.Contain("/a"));
    }

    [Test]
    public async Task TheChainStopsAtTheDepthLimit() {
        StoreLayout();
        Store("/a", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="a"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/page", """
            <% var composition = ctx.Capabilities.GetRequired<IPageComposition>();
               composition.Wrap("/a");
               composition.Wrap(); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page", render: TestOptions.Render with { MaxLayoutDepth = 1 });

        Assert.That(rendered.Body, Does.Contain("<div class=\"a\"><h1>Page</h1></div>"));
        Assert.That(rendered.Body, Does.Not.Contain("<!DOCTYPE html>"), "the second wrapper was past the limit");
    }

    [Test]
    public async Task OneRequestTakesOneSlotAndOneDeadline() {
        StoreLayout();
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page", render: TestOptions.Render with { Slots = 1 });

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
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>
            {{content}}
            """);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RenderAsync("/page", render: TestOptions.Render with { MaxBodyChars = 100 }));

        Assert.That(thrown.Message, Does.Contain("/_layout"));
    }

    [Test]
    public async Task EachLayerReadsItsOwnStorage() {
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               var storage = ctx.Capabilities.GetRequired<IPageStorage>();
               await storage.SetAsync("last", ctx.Path);
               await storage.SetAsync("title", layout.Content?.Title ?? "none"); %><title>Layout</title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               var layout = ctx.Capabilities.GetRequired<IPageLayout>();
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
            <title>Layout</title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><span id="page-path"><%= ctx.Path %></span> <span id="page-request"><%= ctx.Request.Path %></span>
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
                   (await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(new())).Entries; %>
            <title><%= layout.Title ?? "" %></title><span id="layout-count"><%= System.Linq.Enumerable.Count(entries, entry => entry.Hash is not null).ToString() %></span><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page";
               System.Collections.Generic.IReadOnlyList<PageEntry> entries =
                   (await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(new())).Entries; %>
            <span id="page-count"><%= System.Linq.Enumerable.Count(entries, entry => entry.Hash is not null).ToString() %></span>
            """);
        _readStore.Save("/blog", "blog");

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"page-count\">1</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-count\">1</span>"));
    }

    [Test]
    public async Task EveryContentLayerIsOfferedTheWriteAsync() {
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>
            <span id="page-writes"><%= (ctx.Capabilities.Get<IPageWrites>() is null ? "no" : "yes") %></span>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"page-writes\">yes</span>"));
    }

    [Test]
    public async Task ALayoutIsNotOfferedTheWriteAsync() {
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %>
            <title><%= layout.Title ?? "" %></title><span id="layout-writes"><%= (ctx.Capabilities.Get<IPageWrites>() is null ? "no" : "yes") %></span><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-writes\">no</span>"), "a layout is markup and gets neither write");
    }

    [Test]
    public async Task ALayoutThatAsksForTheWriteIsRefusedAsync() {
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               _ = ctx.Capabilities.GetRequired<IPageWrites>(); %>
            <title><%= layout.Title ?? "" %></title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page"));

        Assert.That(refused?.Message, Does.Contain(nameof(IPageWrites)));
    }

    [Test]
    public async Task ALayoutMayReadTheStoreAsync() {
        _readStore.Save("/blog", "<h1>From the store</h1>");
        Store("/_layout", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               PageSource source = (await ctx.Capabilities.GetRequired<IPageCatalog>().ReadAsync("/blog"))!; %>
            <title><%= layout.Title ?? "" %></title><span id="layout-source"><%= source.Source %></span><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
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
            <title><%= layout.Title ?? "" %></title><%== layout.Content?.Body ?? "" %>
            """);
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page";
               _ = await ctx.Capabilities.GetRequired<IPageCatalog>().ReadAsync("/blog"); %><h1>Page</h1>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page", read: TestOptions.Read with { SourceCharsPerSecond = 2 }));

        Assert.That(refused?.Message, Does.Contain(PageReadOptions.RateKey));
    }

    [Test]
    public async Task TheWriteCapabilityIsOfferedToEveryPageAsync() {
        const string Probe = """<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Probe"; %><% ctx.Response.Write(ctx.Capabilities.Get<IPageWrites>() is null ? "absent" : "present"); %>""";
        Store("/pages", Probe);
        Store("/other", Probe);

        Rendered editor = await RenderAsync("/pages", withWrites: true);
        Rendered other = await RenderAsync("/other", withWrites: true);
        Rendered without = await RenderAsync("/other", withWrites: false);

        Assert.Multiple(() => {
            Assert.That(editor.Body, Does.Contain("present"));
            Assert.That(other.Body, Does.Contain("present"), "the editor's key is not what decides who may write");
            Assert.That(without.Body, Does.Contain("absent"), "a host with no page store supplies none");
        });
    }

    [Test]
    public async Task ALayoutIsNeverOfferedTheWriteCapabilityAsync() {
        Store("/pages", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Editor"; %>page
            """);
        Store(DefaultLayout, """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Response.Write(ctx.Capabilities.Get<IPageWrites>() is null ? "absent" : "present"); %>
            <title><%= layout.Title ?? "" %></title><link rel="stylesheet" href="/site.css"><%== layout.Content?.Body ?? "" %></body></html>
            """);

        Rendered rendered = await RenderAsync("/pages", withWrites: true);

        Assert.That(rendered.Body, Does.Contain("absent"));
    }

    private void Store(string path, string template) {
        _storage[path] = new PageTemplate(PagePath.Normalize(path), template);
        _state.LivePages.Add(PagePath.Normalize(path).Key);
    }

    // The wrapper folds the page's title, head and sheets itself, the way the seeded layout does.
    private void StoreLayout(bool allowsScripts = false) {
        Store(DefaultLayout, $$"""
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               var content = layout.Content;
               string title = layout.Title ?? content?.Title ?? "Site";
               var sheets = new System.Collections.Generic.List<string>();
               sheets.AddRange(layout.Stylesheets);
               if (content is not null) { sheets.AddRange(content.Stylesheets); }
               string head = layout.Head + (content?.Head ?? "");
               {{(allowsScripts ? "ctx.Response.AllowScripts();" : "")}} %><!DOCTYPE html><html lang="en"><head><title><%= title %></title><link rel="stylesheet" href="/site.css"><% foreach (string href in sheets) { %><link rel="stylesheet" href="<%= href %>"><% } %><%== head %></head>
            <body>
            <main id="main">
            <%== content?.Body ?? "" %>
            </main>
            </body></html>
            """);
    }

    private async Task<Rendered> RenderAsync(
        string path,
        RenderOptions? render = null,
        bool signedIn = false,
        string query = "",
        PageReadOptions? read = null,
        bool withWrites = true) {

        RenderOptions options = render ?? TestOptions.Render;
        PageSaver saver = CreateSaver(options);
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", path);
        context.Request.QueryString = new QueryString(query);
        if (signedIn) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        }

        PageEnvironment environment = new(
            new ReservedPaths(ReservedPaths.Default),
            TestOptions.Editor,
            TestOptions.Layout with { Default = DefaultLayout },
            options,
            TestContentTypes.Shipped,
            TestOptions.Stores,
            TestOptions.Fetch,
            read ?? TestOptions.Read,
            TestOptions.Scan(),
            _state,
            _messages,
            _objects,
            _readStore,
            TestOptions.Seeds,
            saver,
            _fetcher,
            _metrics,
            _antiforgery,
            new MarkdownRenderer(),
            new DiffRenderer(),
            new FakePageSessionStore(),
            TestSessions.Cookie,
            TestSessions.BrowserCookie,
            TimeProvider.System);

        PageEditor? editor = withWrites ? new PageEditor(_storage, saver) : null;
        await PageEndpoint.HandleAsync(context, CreateRenderer(options), NullLoggerFactory.Instance, environment, editor);

        return new Rendered(context, await TestHttp.ReadBodyAsync(context));
    }

    private PageSaver CreateSaver(RenderOptions options) {
        PageBudget budget = new(options, TestOptions.Compile, TestOptions.Stream);
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageSaver(
            new FakePageRevisionStore(_storage), images, new PageLibraries(new FakePageReadStore()),
            _metrics, NullLogger<PageSaver>.Instance);
    }

    private PageRenderer CreateRenderer(RenderOptions options) {
        PageBudget budget = new(options, TestOptions.Compile, TestOptions.Stream);
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageRenderer(
            _storage,
            _compiler,
            images,
            new PageLibraries(new FakePageReadStore()),
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
