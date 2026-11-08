// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageContentTypesTests {

    private const string Html = "text/html; charset=utf-8";

    [TestCase("/app.js", "text/javascript; charset=utf-8", PageContentKind.JavaScript)]
    [TestCase("/app.mjs", "text/javascript; charset=utf-8", PageContentKind.JavaScript)]
    [TestCase("/app.css", "text/css; charset=utf-8", PageContentKind.Css)]
    [TestCase("/app.json", "application/json; charset=utf-8", PageContentKind.Json)]
    [TestCase("/app.txt", "text/plain; charset=utf-8", PageContentKind.Text)]
    [TestCase("/nested/path/app.js", "text/javascript; charset=utf-8", PageContentKind.JavaScript)]
    [TestCase("/APP.JS", "text/javascript; charset=utf-8", PageContentKind.JavaScript)]
    [TestCase("/v1.2/app.js", "text/javascript; charset=utf-8", PageContentKind.JavaScript)]
    [TestCase("/v1.2/app", Html, PageContentKind.Html)]
    [TestCase("/app", Html, PageContentKind.Html)]
    [TestCase("/app.html", Html, PageContentKind.Html)]
    [TestCase("/app.htm", Html, PageContentKind.Html)]
    [TestCase("/app.svg", Html, PageContentKind.Html)]
    [TestCase("/.js", Html, PageContentKind.Html)]
    [TestCase("/app.", Html, PageContentKind.Html)]
    [TestCase("/app.js/child", Html, PageContentKind.Html)]
    [TestCase("/", Html, PageContentKind.Html)]
    [TestCase(null, Html, PageContentKind.Html)]
    public void ForPathReturnsTheTypeTheKeyNames(string? path, string mediaType, PageContentKind kind) {
        PageContentTypes map = Shipped();

        PageContentType type = map.ForPath(path);

        Assert.That(type.MediaType, Is.EqualTo(mediaType));
        Assert.That(type.Kind, Is.EqualTo(kind));
    }

    [Test]
    public void AConfiguredFallbackAppliesToAnUnmappedKey() {
        PageContentTypes map = PageContentTypes.Create(
            "text/plain; charset=utf-8",
            [new KeyValuePair<string, string?>(".js", "text/javascript")]);

        PageContentType notes = map.ForPath("/notes");
        PageContentType script = map.ForPath("/app.js");

        Assert.That(notes.MediaType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(notes.Kind, Is.EqualTo(PageContentKind.Text));
        Assert.That(script.Kind, Is.EqualTo(PageContentKind.JavaScript));
    }

    [Test]
    public void CreateKeepsTheFallbackAndCountsTheRows() {
        PageContentTypes map = Shipped();

        Assert.That(map.Fallback.MediaType, Is.EqualTo(Html));
        Assert.That(map.Fallback.Kind, Is.EqualTo(PageContentKind.Html));
        Assert.That(map.ExtensionCount, Is.EqualTo(5));
    }

    [Test]
    public void CreateSkipsRowsThatCarryNothing() {
        PageContentTypes map = PageContentTypes.Create(Html, [
            default,
            new KeyValuePair<string, string?>("", ""),
            new KeyValuePair<string, string?>("  ", "  "),
            new KeyValuePair<string, string?>(".js", "text/javascript"),
        ]);

        Assert.That(map.ExtensionCount, Is.EqualTo(1));
        Assert.That(map.ForPath("/app.js").Kind, Is.EqualTo(PageContentKind.JavaScript));
    }

    [TestCase("js")]
    [TestCase("css")]
    [TestCase(".tar.gz")]
    [TestCase(".j s")]
    [TestCase(".js/")]
    [TestCase(".")]
    public void CreateRejectsAKeyThatIsNotOneExtension(string key) {
        Assert.That(
            () => PageContentTypes.Create(Html, [new KeyValuePair<string, string?>(key, "text/javascript")]),
            Throws.ArgumentException);
    }

    [Test]
    public void CreateRejectsTwoRowsThatDifferOnlyInCase() {
        Assert.That(
            () => PageContentTypes.Create(Html, [
                new KeyValuePair<string, string?>(".js", "text/javascript"),
                new KeyValuePair<string, string?>(".JS", "text/javascript"),
            ]),
            Throws.ArgumentException);
    }

    [TestCase("html")]
    [TestCase("text/")]
    [TestCase("/html")]
    [TestCase("text/html/utf-8")]
    [TestCase("text html")]
    [TestCase("text/html;")]
    [TestCase("text/html; charset")]
    public void CreateRejectsAValueThatIsNotAMediaType(string mediaType) {
        Assert.That(
            () => PageContentTypes.Create(Html, [new KeyValuePair<string, string?>(".js", mediaType)]),
            Throws.ArgumentException);
    }

    [Test]
    public void CreateRejectsABlankMediaTypeForARow() {
        Assert.That(
            () => PageContentTypes.Create(Html, [new KeyValuePair<string, string?>(".js", "")]),
            Throws.ArgumentException);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void CreateRejectsABlankFallback(string? fallback) {
        Assert.That(
            () => PageContentTypes.Create(fallback!, []),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void CreateRejectsAMissingRowList() {
        Assert.That(
            () => PageContentTypes.Create(Html, null!),
            Throws.InstanceOf<ArgumentNullException>());
    }

    [TestCase("text/html", PageContentKind.Html)]
    [TestCase("TEXT/HTML", PageContentKind.Html)]
    [TestCase("text/javascript", PageContentKind.JavaScript)]
    [TestCase("application/javascript", PageContentKind.JavaScript)]
    [TestCase("text/css", PageContentKind.Css)]
    [TestCase("text/css; charset=utf-8", PageContentKind.Css)]
    [TestCase("application/json", PageContentKind.Json)]
    [TestCase("application/vnd.api+json", PageContentKind.Json)]
    [TestCase("text/plain", PageContentKind.Text)]
    [TestCase("image/svg+xml", PageContentKind.Other)]
    [TestCase("application/wasm", PageContentKind.Other)]
    public void TheKindIsDerivedFromTheMediaType(string mediaType, PageContentKind kind) {
        Assert.That(PageContentType.FromMediaType(mediaType).Kind, Is.EqualTo(kind));
    }

    [Test]
    public void TheShippedJavaScriptTypeIsPinned() {
        PageContentType type = Shipped().ForPath("/app.js");

        Assert.That(type.MediaType, Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(type.MediaType, Is.Not.EqualTo("application/javascript"));
    }

    private static PageContentTypes Shipped() => PageContentTypes.Create(Html, new Dictionary<string, string?> {
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    });
}
