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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
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
            <% ctx.Features.GetRequired<IPageLayout>().Layout = null; %><h1>Alone</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Alone</h1>"));
    }

    [Test]
    public async Task APageMayChooseAnotherLayout() {
        Store("/other", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>(); %><%== PageDocument.Head(layout.Title) %><p class="other-layout"></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><% ctx.Redirect("/elsewhere"); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.Headers.Location.ToString(), Is.EqualTo("/elsewhere"));
        Assert.That(rendered.Body, Is.Empty);
    }

    [Test]
    public async Task TheContentRunsBeforeItsLayout() {
        StoreLayout();
        Store("/page", """
            <% await ctx.Features.GetRequired<IPageStorage>().SetAsync("ran", "content");
               ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<h1>Page</h1>"));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/page"), "ran"), Is.EqualTo("\"content\""));
    }

    [Test]
    public async Task TheLayoutReceivesTheContentAsItsBody() {
        StoreLayout();
        Store("/page", """
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><p class="content">hello</p>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<p class=\"content\">hello</p>"));
    }

    [Test]
    public async Task ATitleOrHeadValueSetByTheContentReachesTheLayout() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "The Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<title>The Page</title>"));
    }

    [Test]
    public async Task AWrappedPageThatSetsNoTitleIsAnErrorNamingThePage() {
        StoreLayout();
        Store("/untitled", "<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("/untitled"));
        Assert.That(rendered.Body, Does.Not.Contain("<h1>Forgot</h1>"));
    }

    [Test]
    public async Task AnUntitledPageOffersItsEditorToTheSignedInOwner() {
        StoreLayout();
        Store("/untitled", "<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled", signedIn: true);

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        Assert.That(rendered.Body, Does.Contain("href=\"/edit/untitled\">Edit this page"));
    }

    [Test]
    public async Task AnUntitledPageOffersNoEditorToAnAnonymousVisitor() {
        StoreLayout();
        Store("/untitled", "<h1>Forgot</h1>");

        Rendered rendered = await RenderAsync("/untitled");

        Assert.That(rendered.Body, Does.Not.Contain("href=\"/edit/untitled\""));
    }

    [Test]
    public void ReadingATitleThatWasNeverSetThrows() {
        PageLayoutState layout = new(DefaultLayout);

        Assert.Throws<InvalidOperationException>(() => _ = layout.Title);
    }

    [Test]
    public async Task TheOutermostLayoutWritesTheTitle() {
        Store("/outer", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "Site — " + layout.Title; %><%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.Title = "Middle — " + layout.Title; %><p class="middle"></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.AddHead("<meta name=\"outer\" content=\"1\">"); %><%== PageDocument.Head(layout.Title, head: layout.Head) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.AddHead("<meta name=\"middle\" content=\"1\">"); %><p></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.AddStylesheet("/outer.css"); %><%== PageDocument.Head(layout.Title, layout.Stylesheets) %><%== layout.Body %>
            """);
        Store("/middle", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/outer";
               layout.AddStylesheet("/middle.css"); %><p></p><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/middle";
               layout.Title = "Page";
               layout.AddStylesheet("/page.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])[
            "/layout.css", "/site.css", "/outer.css", "/middle.css", "/page.css",
        ]));
    }

    [Test]
    public async Task APageThatAddsNoSheetAddsNone() {
        StoreLayout();
        Store("/page", """
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])["/layout.css", "/site.css"]));
    }

    [Test]
    public async Task TwoPagesSheetsKeepTheOrderTheyWereAddedIn() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "Page";
               layout.AddStylesheet("/second.css");
               layout.AddStylesheet("/first.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])[
            "/layout.css", "/site.css", "/second.css", "/first.css",
        ]));
    }

    [Test]
    public async Task TheSameSheetFromTwoLayersIsLinkedOnce() {
        StoreLayout();
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "Page";
               layout.AddStylesheet("/site.css"); %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(Sheets(rendered.Body), Is.EqualTo((string[])["/layout.css", "/site.css"]));
    }

    [Test]
    public async Task TheContentOwnsTheStatusTheContentTypeAndTheHeaders() {
        // The layout is the one that tries, so it is the layout that must be ignored.
        Store(DefaultLayout, """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               ctx.SetStatus(418);
               ctx.SetContentType("text/plain");
               ctx.SetHeader("X-Layout", "1"); %><%== PageDocument.Head(layout.Title) %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(rendered.Body.Trim(), Is.EqualTo("<h1>Page</h1>"));
    }

    [Test]
    public async Task AMissingExplicitLayoutIsAnErrorNamingTheKey() {
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% var layout = ctx.Features.GetRequired<IPageLayout>(); %><div class="outer"><%== layout.Body %></div>
            """);
        Store("/inner", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/outer"; %><div class="inner"><%== layout.Body %></div>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/b"; %><i>a</i><%== layout.Body %>
            """);
        Store("/b", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Layout = "/a"; %><i>b</i><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %><h1>Page</h1>
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
            <% ctx.Features.GetRequired<IPageLayout>().Title = "Page"; %>
            {{content}}
            """);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RenderAsync("/page", render: new RenderOptions { MaxBodyChars = 100 }));

        Assert.That(thrown.Message, Does.Contain("/_layout"));
    }

    [Test]
    public async Task EachLayerReadsItsOwnStorage() {
        Store(DefaultLayout, """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               var storage = ctx.Features.GetRequired<IPageStorage>();
               await storage.SetAsync("last", ctx.Path);
               await storage.SetAsync("title", layout.Title); %><%== PageDocument.Head("Layout") %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "Page";
               await ctx.Features.GetRequired<IPageStorage>().SetAsync("last", ctx.Path); %><h1>Page</h1>
            """);

        await RenderAsync("/page");

        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/page"), "last"), Is.EqualTo("\"/page\""));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/_layout"), "last"), Is.EqualTo("\"/_layout\""));
        Assert.That(await _state.GetValueAsync(PagePath.Normalize("/_layout"), "title"), Is.EqualTo("\"Page\""));
    }

    [Test]
    public async Task TheLayoutSeesTheRequestedPathAndItsOwnKey() {
        Store(DefaultLayout, """
            <% var layout = ctx.Features.GetRequired<IPageLayout>(); %><span id="layout-path"><%= ctx.Path %></span> <span id="layout-request"><%= ctx.Request.Path %></span>
            <%== PageDocument.Head("Layout") %><%== layout.Body %>
            """);
        Store("/page", """
            <% var layout = ctx.Features.GetRequired<IPageLayout>();
               layout.Title = "Page"; %><span id="page-path"><%= ctx.Path %></span> <span id="page-request"><%= ctx.Request.Path %></span>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-path\">/_layout</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"layout-request\">/page</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"page-path\">/page</span>"));
        Assert.That(rendered.Body, Does.Contain("<span id=\"page-request\">/page</span>"));
    }

    private void Store(string path, string template) {
        _storage[path] = new PageTemplate(PagePath.Normalize(path), template);
        _state.LivePages.Add(PagePath.Normalize(path).Key);
    }

    private void StoreLayout(bool allowsScripts = false) {
        Store(DefaultLayout, $$"""
            <% var layout = ctx.Features.GetRequired<IPageLayout>(); {{(allowsScripts ? "ctx.AllowScripts();" : "")}} %><%== PageDocument.Head(layout.Title, layout.Stylesheets, head: layout.Head) %>
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
        string query = "") {

        RenderOptions options = render ?? new RenderOptions();
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", path);
        context.Request.QueryString = new QueryString(query);
        if (signedIn) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        }

        PageEnvironment environment = new(
            new ReservedPaths(ReservedPaths.Default),
            new PageLayoutConfiguration { Default = DefaultLayout },
            options,
            TestContentTypes.Shipped,
            new PageStorageOptions(),
            new PageFetchOptions(),
            _state,
            _fetcher,
            _metrics,
            _antiforgery,
            new MarkdownRenderer());

        await PageEndpoint.HandleAsync(context, CreateRenderer(options), NullLoggerFactory.Instance, environment);

        return new Rendered(context, await TestHttp.ReadBodyAsync(context));
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
