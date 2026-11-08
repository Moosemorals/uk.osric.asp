// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageContentTypesTests {

    private const string Html = "text/html; charset=utf-8";

    [TestCase("/app.js", "text/javascript; charset=utf-8")]
    [TestCase("/app.mjs", "text/javascript; charset=utf-8")]
    [TestCase("/app.css", "text/css; charset=utf-8")]
    [TestCase("/app.json", "application/json; charset=utf-8")]
    [TestCase("/app.txt", "text/plain; charset=utf-8")]
    [TestCase("/nested/path/app.js", "text/javascript; charset=utf-8")]
    [TestCase("/APP.JS", "text/javascript; charset=utf-8")]
    [TestCase("/v1.2/app.js", "text/javascript; charset=utf-8")]
    [TestCase("/v1.2/app", Html)]
    [TestCase("/app", Html)]
    [TestCase("/app.html", Html)]
    [TestCase("/app.htm", Html)]
    [TestCase("/app.svg", Html)]
    [TestCase("/.js", Html)]
    [TestCase("/app.", Html)]
    [TestCase("/app.js/child", Html)]
    [TestCase("/", Html)]
    [TestCase(null, Html)]
    public void ForPathReturnsTheTypeTheKeyNames(string? path, string mediaType) {
        Assert.That(Shipped().ForPath(path), Is.EqualTo(mediaType));
    }

    [Test]
    public void AConfiguredFallbackAppliesToAnUnmappedKey() {
        PageContentTypes map = PageContentTypes.Create(
            "text/plain; charset=utf-8",
            [new KeyValuePair<string, string?>(".js", "text/javascript")]);

        Assert.That(map.ForPath("/notes"), Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(map.ForPath("/app.js"), Is.EqualTo("text/javascript"));
    }

    [Test]
    public void CreateKeepsTheFallbackAndCountsTheRows() {
        PageContentTypes map = Shipped();

        Assert.That(map.Fallback, Is.EqualTo(Html));
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
        Assert.That(map.ForPath("/app.js"), Is.EqualTo("text/javascript"));
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

    [Test]
    public void TheShippedJavaScriptTypeIsPinned() {
        string type = Shipped().ForPath("/app.js");

        Assert.That(type, Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(type, Is.Not.EqualTo("application/javascript"));
    }

    private static PageContentTypes Shipped() => PageContentTypes.Create(Html, new Dictionary<string, string?> {
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    });
}
