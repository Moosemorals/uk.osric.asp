// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using uk.osric.asp.Web.Html;

[TestFixture]
public sealed class ShellDocumentTests {

    [Test]
    public async Task HeadStatesTheLanguageAndTheTitleAsync() {
        string body = await RenderAsync(title: "Editor <source>");

        MarkupReader document = MarkupReader.Parse(body);
        Assert.That(document.Elements("html").Single().Attribute("lang"), Is.EqualTo("en"));
        Assert.That(document.Elements("title").Single().Text.Trim(), Is.EqualTo("Editor <source>"));
    }

    [Test]
    public async Task SheetsAreLinkedLayoutThenSiteThenViewAsync() {
        string[] sheets = [.. MarkupReader.Parse(await RenderAsync(stylesheet: "/editor.css"))
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
    public async Task BodyTakesAClassOnlyWhenAskedAsync() {
        Assert.That(MarkupReader.Parse(await RenderAsync()).Elements("body").Single().HasAttribute("class"), Is.False);
        Assert.That(MarkupReader.Parse(await RenderAsync(bodyClass: "editor-page")).Elements("body").Single().Attribute("class"),
            Is.EqualTo("editor-page"));
    }

    [Test]
    public async Task TheBodyOpensOnTheOnlyMainLandmarkAsync() {
        MarkupReader document = MarkupReader.Parse(await RenderAsync());

        MarkupElement body = document.Elements("body").Single();
        MarkupElement main = document.Elements("main").Single();

        Assert.That(main.Attribute("id"), Is.EqualTo("main"));
        Assert.That(body.Children, Has.Count.EqualTo(1));
        Assert.That(body.Children[0].Name, Is.EqualTo("main"));
    }

    [Test]
    public async Task TheDocumentIsHtmlThatIsNeverStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, "Error", StatusCodes.Status500InternalServerError);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task AViewWithAntiforgeryKeepsTheFrameworkCachePolicyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";

        await ShellDocument.BeginAsync(context, "Editor");

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache, no-store"));
        Assert.That(context.Response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
    }

    [Test]
    public async Task TheEndClosesTheLandmarkAroundWhatTheViewWroteAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, "Title");
        await context.Response.WriteAsync("<h1>Body</h1>");
        await ShellDocument.EndAsync(context);

        MarkupReader document = MarkupReader.Parse(await TestHttp.ReadBodyAsync(context));
        MarkupElement main = document.Elements("main").Single();

        Assert.That(main.Children.Single().Name, Is.EqualTo("h1"));
        Assert.That(main.Children.Single().Text.Trim(), Is.EqualTo("Body"));
        Assert.That(document.Elements("body").Single().Children, Has.Count.EqualTo(1));
    }

    private static async Task<string> RenderAsync(
        string title = "Title",
        string? stylesheet = null,
        string? bodyClass = null) {

        DefaultHttpContext context = TestHttp.CreateContext(null);

        await ShellDocument.BeginAsync(context, title, stylesheet: stylesheet, bodyClass: bodyClass);

        return await TestHttp.ReadBodyAsync(context);
    }
}
