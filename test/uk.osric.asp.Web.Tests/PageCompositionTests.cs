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

/// <summary>A page rendered inside, or around, another one.</summary>
/// <remarks>
/// Every test drives <see cref="PageEndpoint.HandleAsync"/>, because composition runs in the render
/// the endpoint opened: the same slot, deadline and body allowance.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageCompositionTests {

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
    public async Task AnEmbeddedPageIsWrittenWhereTheCallIs() {
        Store("/inner", """<p class="inner">inner</p>""");
        Store("/page", """
            <h1>Outer</h1>
            <% await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner"); %>
            <p class="after">after</p>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.IndexOf("<p class=\"inner\">inner</p>", StringComparison.Ordinal),
            Is.GreaterThan(rendered.Body.IndexOf("<h1>Outer</h1>", StringComparison.Ordinal)));
        Assert.That(rendered.Body.IndexOf("<p class=\"after\">after</p>", StringComparison.Ordinal),
            Is.GreaterThan(rendered.Body.IndexOf("<p class=\"inner\">inner</p>", StringComparison.Ordinal)));
    }

    [Test]
    public async Task AnEmbedReturnsTheFrameAndWritesTheBodyOnce() {
        Store("/inner", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Inner";
               layout.AddStylesheet("/inner.css");
               layout.AddHead("<meta name=\"inner\">"); %><p>inner</p>
            """);
        Store("/page", """
            <% PageFrame frame = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner");
               ctx.Response.Write("title:" + (frame.Title ?? "none"));
               ctx.Response.Write(" body:" + string.Join(",", frame.Stylesheets));
               ctx.Response.Write(" head:" + frame.Head); %>
            <p>inner</p>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(CountOf(rendered.Body, "<p>inner</p>"), Is.EqualTo(2),
                "the body comes from the write, and this page's own copy makes two");
            Assert.That(rendered.Body, Does.Contain("title:Inner"), "the frame carries the title");
            Assert.That(rendered.Body, Does.Contain("body:/inner.css"));
            Assert.That(rendered.Body, Does.Contain("head:&lt;meta name=&quot;inner&quot;&gt;"));
        });
    }

    [Test]
    public async Task AnEmbeddedPageRunsItsOwnWrappers() {
        Store("/wrap", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><div class="wrap"><%== layout.Content?.Body ?? "" %></div>
            """);
        Store("/inner", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/wrap"); %><p>inner</p>
            """);
        Store("/page", """
            <% PageFrame frame = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner");
               ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Contains("<div class=\"wrap\"><p>inner</p></div>", StringComparison.Ordinal), Is.True,
            rendered.Body);
    }

    [Test]
    public async Task ACaptureReturnsTheBodyAndWritesNothing() {
        Store("/draft", """<p>draft</p>""");
        Store("/page", """
            <% PageCapture captured = await ctx.Capabilities.GetRequired<IPageComposition>().CaptureAsync("/draft");
               ctx.Response.WriteRaw(captured.Body.ToUpperInvariant()); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("<P>DRAFT</P>"));
    }

    [Test]
    public async Task ACapturedPagesResponseStaysWithTheCapturedPage() {
        Store("/loud", """
            <% ctx.Response.SetStatus(418);
               ctx.Response.SetContentType("text/plain");
               ctx.Response.SetHeader("X-Loud", "1"); %><p>loud</p>
            """);
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().CaptureAsync("/loud"); %>quiet
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(rendered.Context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(rendered.Context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(rendered.Context.Response.Headers["X-Loud"], Is.Empty);
        });
    }

    [Test]
    public async Task ACapturesHeadSheetsAndScriptPermissionArriveOnlyInTheValue() {
        Store("/loud", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Loud";
               layout.AddHead("<meta name=\"loud\">");
               layout.AddStylesheet("/loud.css");
               ctx.Response.AllowScripts(); %><p>loud</p>
            """);
        Store("/page", """
            <% PageCapture captured = await ctx.Capabilities.GetRequired<IPageComposition>().CaptureAsync("/loud");
               ctx.Response.WriteRaw(captured.Title ?? "");
               ctx.Response.WriteRaw(captured.Head);
               foreach (string href in captured.Stylesheets) { ctx.Response.WriteRaw(href); }
               ctx.Response.WriteRaw(captured.AllowsScripts ? "scripts" : "no-scripts"); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("Loud"));
            Assert.That(rendered.Body, Does.Contain("<meta name=\"loud\">"));
            Assert.That(rendered.Body, Does.Contain("/loud.css"));
            Assert.That(rendered.Body, Does.Contain("scripts"));
        });
    }

    [Test]
    public async Task AnEmbeddedPageSeesItsOwnKeyAndTheVisitorsPage() {
        Store("/inner", """<%= ctx.Path %>|<%= ctx.Request.Path %>""");
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner"); %>
            """);

        Rendered rendered = await RenderAsync("/page", query: "?x=1");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("/inner|/page"));
    }

    [Test]
    public async Task AnEmbeddedPageRunsTheRequestItIsGiven() {
        Store("/inner", """<%= ctx.Request.Query.TryGetValue("id", out string? id) ? id ?? "" : "none" %>""");
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync(
                   "/inner", ctx.Request.WithQuery("id", "7")); %>
            """);

        Rendered rendered = await RenderAsync("/page", query: "?id=1");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("7"));
    }

    [Test]
    public async Task AnEmbeddedPageForwardsTheCallersRequestWhenNoneIsGiven() {
        Store("/inner", """<%= ctx.Request.Query.TryGetValue("id", out string? id) ? id ?? "" : "none" %>""");
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner"); %>
            """);

        Rendered rendered = await RenderAsync("/page", query: "?id=3");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("3"));
    }

    [Test]
    public async Task AComposedPageCannotChangeTheVisitorsPath() {
        Store("/inner", """<%= ctx.Request.Path %>""");
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync(
                   "/inner", ctx.Request.WithQuery("id", "7") with { Path = "/elsewhere" }); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("/page"));
    }

    [Test]
    public async Task AnEmbeddedPageThatRedirectsIsRefusedAndTheCallerMayCatchIt() {
        Store("/inner", """<% ctx.Response.Redirect("/elsewhere"); %>""");
        Store("/page", """
            <% try {
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner");
                   ctx.Response.Write("no refusal");
               } catch (UserFacingException refused) {
                   ctx.Response.Write("refused:" + refused.Message.Contains("/inner").ToString());
               } %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("refused:True"));
    }

    [Test]
    public async Task AMissingEmbeddedPageIsAUserFacingExceptionNamingTheKey() {
        Store("/page", """
            <% try {
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/gone");
                   ctx.Response.Write("no refusal");
               } catch (UserFacingException refused) {
                   ctx.Response.Write("refused:" + refused.Message.Contains("/gone").ToString());
               } %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("refused:True"));
    }

    [Test]
    public async Task ANoCodePageEmbedsAsItsFragmentWithoutItsComments() {
        Store("/plain", "<p>plain</p><%# a note %>");
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/plain"); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("<p>plain</p>"));
            Assert.That(rendered.Body, Does.Not.Contain("a note"));
        });
    }

    [Test]
    public async Task APageThatEmbedsItselfIsRefused() {
        Store("/loop", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/loop"); %>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/loop"));

        Assert.That(refused?.Message, Does.Contain("/loop"));
    }

    [Test]
    public async Task APageThatEmbedsItselfWithAnotherQueryIsAllowedAndCounted() {
        using MetricCapture capture = MetricCapture.Start();
        Store("/list", """
            <% string? id = ctx.Request.Query.TryGetValue("id", out string? given) ? given : null;
               if (id is null) { ctx.Response.WriteRaw("root"); }
               else if (id != "1") {
                   ctx.Response.WriteRaw("<p>" + id + "</p>");
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync(
                       "/list", ctx.Request.WithQuery("id", "1"));
               } else { ctx.Response.WriteRaw("<p>leaf</p>"); } %>
            """);
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync(
                   "/list", ctx.Request.WithQuery("id", "2")); %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(rendered.Body, Does.Contain("<p>2</p>"));
            Assert.That(rendered.Body, Does.Contain("<p>leaf</p>"));
            Assert.That(capture.Count("page.composition.recursive", "page.path", "/list"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AListingMayEmbedTheSameCardManyTimes() {
        Store("/card", """<p class="card">card</p>""");
        Store("/page", """
            <% for (int index = 0; index < 3; index++) {
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/card");
               } %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(CountOf(rendered.Body, "class=\"card\""), Is.EqualTo(3));
    }

    [Test]
    public async Task TheFanOutCapStopsOneEmbedPastItsLimit() {
        Store("/card", """<p class="card">card</p>""");
        Store("/page", """
            <% for (int index = 0; index < 3; index++) {
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/card");
               } %>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page", render: TestOptions.Render with { MaxEmbeds = 2 }));

        Assert.That(refused?.Message, Does.Contain("MaxEmbeds"));
    }

    [Test]
    public async Task TheDepthCapStopsANestedEmbed() {
        Store("/one", """<% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/two"); %>""");
        Store("/two", """<p>two</p>""");
        Store("/page", """<% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/one"); %>""");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page", render: TestOptions.Render with { MaxEmbedDepth = 1 }));

        Assert.That(refused?.Message, Does.Contain("MaxEmbedDepth"));
    }

    [Test]
    public async Task AnEmbedOverTheRemainingAllowanceLeavesTheCallersBodyUntouched() {
        Store("/inner", new string('x', 200));
        Store("/page", """
            <% ctx.Response.Write("before");
               try {
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/inner");
               } catch (System.InvalidOperationException) {
                   ctx.Response.Write("|refused");
               } %>
            """);

        Rendered rendered = await RenderAsync("/page", render: TestOptions.Render with { MaxBodyChars = 100 });

        Assert.That(rendered.Body, Is.EqualTo("before|refused"), "a refused embed wrote nothing");
    }

    [Test]
    public async Task TwoEmbeddedWritersEachWriteTheirOwnPage() {
        Store("/saver", """
            <% if (ctx.User is not null) {
                   await ctx.Capabilities.GetRequired<IPageWrites>().SaveAsync(ctx.Path, "saved", null);
               } %>saved
            """);
        Store("/page", """
            <% if (ctx.User is not null) {
                   try {
                       _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/saver");
                       _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/saver");
                       ctx.Response.Write("two writes");
                   } catch (System.InvalidOperationException) {
                       ctx.Response.Write("refused");
                   }
               } %>
            """);

        Rendered rendered = await RenderAsync("/page", signedIn: true);

        Assert.That(rendered.Body.Trim(), Is.EqualTo("savedsavedtwo writes"), rendered.Body);
    }

    [Test]
    public async Task AnEmbeddedWriteInsideThePagesTransactionLandsWithIt() {
        Store("/widget", """
            <% if (ctx.User is not null) {
                   await using (IPageTransaction own = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction()) {
                       _ = await own.SaveAsync("/widget-saved", "text", null);
                       _ = await own.CommitAsync();
                   }
                   ctx.Response.Write("wrote");
               } %>
            """);
        Store("/page", """
            <% if (ctx.User is not null) {
                   await using IPageTransaction held = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction();
                   _ = await held.SaveAsync("/page-saved", "page", null);
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/widget");
                   _ = await held.CommitAsync();
               } %>
            """);

        Rendered rendered = await RenderAsync("/page", signedIn: true);

        Assert.Multiple(() => {
            Assert.That(rendered.Body.Trim(), Is.EqualTo("wrote"), rendered.Body);
            Assert.That(_storage["/page-saved"]?.Template, Is.EqualTo("page"));
            Assert.That(_storage["/widget-saved"]?.Template, Is.EqualTo("text"), "the embedded transaction nested inside the page's");
        });
    }

    [Test]
    public async Task AnEmbeddedWriteIsLostWhenThePagesTransactionIsDisposed() {
        Store("/widget", """
            <% if (ctx.User is not null) {
                   await using (IPageTransaction own = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction()) {
                       _ = await own.SaveAsync("/widget-saved", "text", null);
                       _ = await own.CommitAsync();
                   }
               } %>
            """);
        Store("/page", """
            <% if (ctx.User is not null) {
                   await using IPageTransaction held = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction();
                   _ = await held.SaveAsync("/page-saved", "page", null);
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/widget");
               } %>
            """);

        _ = await RenderAsync("/page", signedIn: true);

        Assert.Multiple(() => {
            Assert.That(_storage["/page-saved"], Is.Null);
            Assert.That(_storage["/widget-saved"], Is.Null, "the nested write never landed on its own");
        });
    }

    [Test]
    public async Task AnEmbeddedWriteLandsOnceTheOuterTransactionIsCommitted() {
        Store("/widget", """
            <% if (ctx.User is not null) {
                   PageEditResult saved = await ctx.Capabilities.GetRequired<IPageWrites>().SaveAsync("/widget-saved", "text", null);
                   ctx.Response.Write(saved.Outcome.ToString());
               } %>
            """);
        Store("/page", """
            <% if (ctx.User is not null) {
                   await using (IPageTransaction held = ctx.Capabilities.GetRequired<IPageWrites>().BeginTransaction()) {
                       _ = await held.SaveAsync("/page-saved", "page", null);
                       _ = await held.CommitAsync();
                   }
                   _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/widget");
               } %>
            """);

        Rendered rendered = await RenderAsync("/page", signedIn: true);

        Assert.Multiple(() => {
            Assert.That(rendered.Body.Trim(), Is.EqualTo("Saved"), rendered.Body);
            Assert.That(_storage["/page-saved"]?.Template, Is.EqualTo("page"));
            Assert.That(_storage["/widget-saved"]?.Template, Is.EqualTo("text"));
        });
    }

    [Test]
    public async Task AnEmbeddedWriterIsOfferedTheWriteItself() {
        Store("/probe", """
            <%= ctx.Capabilities.Get<IPageWrites>() is null ? "absent" : "present" %>
            """);
        Store("/page", """
            <% _ = await ctx.Capabilities.GetRequired<IPageComposition>().EmbedAsync("/probe"); %>
            """);

        Rendered rendered = await RenderAsync("/page", signedIn: true);

        Assert.That(rendered.Body.Trim(), Is.EqualTo("present"));
    }

    [Test]
    public async Task AWrapNamesAReservedKeyIsRefusedAtTheCall() {
        Store("/page", """
            <% try {
                   ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/login");
                   ctx.Response.Write("no refusal");
               } catch (System.InvalidOperationException) {
                   ctx.Response.Write("refused");
               } %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("refused"));
    }

    [Test]
    public async Task AWrapWithAMalformedKeyIsRefusedAtTheCall() {
        Store("/page", """
            <% try {
                   ctx.Capabilities.GetRequired<IPageComposition>().Wrap("/a?b=c");
                   ctx.Response.Write("no refusal");
               } catch (System.ArgumentException) {
                   ctx.Response.Write("refused");
               } %>
            """);

        Rendered rendered = await RenderAsync("/page");

        Assert.That(rendered.Body.Trim(), Is.EqualTo("refused"), rendered.Body);
    }

    [Test]
    public async Task AWrapWithNoConfiguredLayoutRecordsNothing() {
        Store("/page", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %><p>alone</p>
            """);

        Rendered rendered = await RenderAsync("/page", layout: null);

        Assert.That(rendered.Body.Trim(), Is.EqualTo("<p>alone</p>"));
    }

    private void Store(string path, string template) {
        _storage[path] = new PageTemplate(PagePath.Normalize(path), template);
        _state.LivePages.Add(PagePath.Normalize(path).Key);
    }

    private async Task<Rendered> RenderAsync(
        string path,
        RenderOptions? render = null,
        bool signedIn = false,
        string query = "",
        string? layout = DefaultLayout) {

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
            TestOptions.Layout with { Default = layout },
            options,
            TestContentTypes.Shipped,
            TestOptions.Storage,
            TestOptions.Fetch,
            TestOptions.Read,
            _state,
            _readStore,
            TestOptions.Seeds,
            saver,
            _fetcher,
            _metrics,
            _antiforgery,
            new MarkdownRenderer(),
            new DiffRenderer(),
            TestOptions.Session,
            new FakePageSessionStore(),
            TestSessions.Cookie);

        PageEditor editor = new(_storage, saver);
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

    private sealed record Rendered(DefaultHttpContext Context, string Body);
}
