// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Web.Html;

[TestFixture]
public sealed class ShellDocumentTests {

    private static readonly string Navigation = SiteNavigation.Nav(
        authenticated: true, assistantEnabled: false, path: "/edit", query: string.Empty);

    [Test]
    public async Task HeadStatesTheLanguageAndTheTitleAsync() {
        string body = await RenderAsync(title: "Editor <source>");

        MarkupReader document = MarkupReader.Parse(body);
        Assert.That(document.Elements("html").Single().Attribute("lang"), Is.EqualTo("en"));
        Assert.That(document.Elements("title").Single().Text.Trim(), Is.EqualTo("Editor <source>"));
    }

    [Test]
    public async Task SheetsAreLinkedLayoutThenSiteThenViewAsync() {
        string[] sheets = [.. MarkupReader.Parse(await RenderAsync(stylesheets: ["/editor.css"]))
            .Elements("link")
            .Where(link => string.Equals(link.Attribute("rel"), "stylesheet", StringComparison.Ordinal))
            .Select(link => link.Attribute("href")!)];

        string[] expected = ["/layout.css", "/site.css", "/editor.css"];
        Assert.That(sheets, Is.EqualTo(expected));
    }

    [Test]
    public async Task AViewWithNoSheetOfItsOwnGetsTheSharedOnesAsync() {
        string[] sheets = [.. MarkupReader.Parse(await RenderAsync())
            .Elements("link")
            .Where(link => string.Equals(link.Attribute("rel"), "stylesheet", StringComparison.Ordinal))
            .Select(link => link.Attribute("href")!)];

        string[] expected = ["/layout.css", "/site.css"];
        Assert.That(sheets, Is.EqualTo(expected));
    }

    [Test]
    public async Task AViewMayAskForSeveralSheetsInTheOrderItAskedAsync() {
        string[] sheets = [.. MarkupReader.Parse(await RenderAsync(stylesheets: ["/first.css", "/second.css"]))
            .Elements("link")
            .Where(link => string.Equals(link.Attribute("rel"), "stylesheet", StringComparison.Ordinal))
            .Select(link => link.Attribute("href")!)];

        string[] expected = ["/layout.css", "/site.css", "/first.css", "/second.css"];
        Assert.That(sheets, Is.EqualTo(expected));
    }

    [Test]
    public async Task BodyTakesAClassOnlyWhenAskedAsync() {
        Assert.That(MarkupReader.Parse(await RenderAsync()).Elements("body").Single().HasAttribute("class"), Is.False);
        Assert.That(MarkupReader.Parse(await RenderAsync(bodyClass: "editor-page")).Elements("body").Single().Attribute("class"),
            Is.EqualTo("editor-page"));
    }

    [Test]
    public async Task TheSkipLinkIsTheFirstThingInTheBodyAndTargetsMainAsync() {
        MarkupReader document = MarkupReader.Parse(await RenderAsync());

        MarkupElement first = document.Elements("body").Single().Children[0];
        Assert.That(first.Name, Is.EqualTo("a"));
        Assert.That(first.Attribute("href"), Is.EqualTo("#main"));
        Assert.That(first.Attribute("class"), Is.EqualTo("skip-link"));
        Assert.That(first.Text.Trim(), Is.Not.Empty);
    }

    [Test]
    public async Task TheBannerPrecedesTheOnlyMainLandmarkAsync() {
        MarkupReader document = MarkupReader.Parse(await RenderAsync());

        IReadOnlyList<MarkupElement> children = document.Elements("body").Single().Children;
        Assert.That(children[1].Name, Is.EqualTo("header"));
        Assert.That(children[1].Children.Single().Name, Is.EqualTo("nav"));
        Assert.That(children[2].Name, Is.EqualTo("main"));
        Assert.That(document.Elements("main"), Has.Count.EqualTo(1));
        Assert.That(document.Elements("main").Single().Attribute("id"), Is.EqualTo("main"));
    }

    [Test]
    public async Task TheDocumentIsHtmlThatIsNeverStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, "Error", Navigation, StatusCodes.Status500InternalServerError);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task AViewWithAntiforgeryKeepsTheFrameworkCachePolicyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";

        await ShellDocument.BeginAsync(context, "Editor", Navigation);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache, no-store"));
        Assert.That(context.Response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
    }

    [Test]
    public async Task TheEndClosesTheLandmarkAroundWhatTheViewWroteAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, "Title", Navigation);
        await context.Response.WriteAsync("<h1>Body</h1>");
        await ShellDocument.EndAsync(context);

        MarkupReader document = MarkupReader.Parse(await TestHttp.ReadBodyAsync(context));
        MarkupElement main = document.Elements("main").Single();

        Assert.That(main.Children.Single().Name, Is.EqualTo("h1"));
        Assert.That(main.Children.Single().Text.Trim(), Is.EqualTo("Body"));
        Assert.That(document.Elements("body").Single().Children.Last().Name, Is.EqualTo("main"));
    }

    private static async Task<string> RenderAsync(
        string title = "Title",
        IReadOnlyList<string>? stylesheets = null,
        string? bodyClass = null) {

        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, title, Navigation, stylesheets: stylesheets, bodyClass: bodyClass);

        return await TestHttp.ReadBodyAsync(context);
    }
}
