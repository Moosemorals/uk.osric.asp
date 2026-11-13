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
        Assert.That(map.ExtensionCount, Is.EqualTo(6));
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

    [Test]
    public void StringRowsTakeTheNoScriptPolicy() {
        PageContentTypes map = PageContentTypes.Create(
            Html,
            [new KeyValuePair<string, string?>(".js", "text/javascript")]);

        Assert.That(map.ScriptsForPath("/app.js"), Is.EqualTo(PageScriptPolicy.None));
        Assert.That(map.ScriptsForPath("/notes"), Is.EqualTo(PageScriptPolicy.None));
    }

    [Test]
    public void ARowCarriesItsScriptPolicy() {
        PageContentTypes map = PageContentTypes.Create(
            new PageContentTypeRule(Html),
            [new KeyValuePair<string, PageContentTypeRule>(
                ".htmlx", new PageContentTypeRule(Html, PageScriptPolicy.Self))]);

        Assert.That(map.ScriptsForPath("/page.htmlx"), Is.EqualTo(PageScriptPolicy.Self));
        Assert.That(map.ScriptsForPath("/page"), Is.EqualTo(PageScriptPolicy.None));
        Assert.That(map.ScriptsForPath("/page.html"), Is.EqualTo(PageScriptPolicy.None));
    }

    [Test]
    public void TheFallbackCarriesItsScriptPolicy() {
        PageContentTypes map = PageContentTypes.Create(
            new PageContentTypeRule("text/plain; charset=utf-8", PageScriptPolicy.Self),
            []);

        Assert.That(map.ScriptsForPath("/notes"), Is.EqualTo(PageScriptPolicy.Self));
        Assert.That(map.FallbackRule.ScriptPolicy, Is.EqualTo(PageScriptPolicy.Self));
    }

    [Test]
    public void RuleForReturnsTheWholeRow() {
        PageContentTypeRule row = new("text/html; charset=utf-8", PageScriptPolicy.Self);
        PageContentTypes map = PageContentTypes.Create(
            new PageContentTypeRule(Html),
            [new KeyValuePair<string, PageContentTypeRule>(".htmlx", row)]);

        Assert.That(map.RuleFor("/page.htmlx"), Is.EqualTo(row));
        Assert.That(map.RuleFor("/page"), Is.EqualTo(new PageContentTypeRule(Html)));
    }

    [Test]
    public void AnUnknownScriptPolicyIsRejected() {
        Assert.That(
            () => PageContentTypes.Create(
                new PageContentTypeRule(Html),
                [new KeyValuePair<string, PageContentTypeRule>(
                    ".htmlx", new PageContentTypeRule(Html, (PageScriptPolicy)42))]),
            Throws.ArgumentException);
    }

    [Test]
    public void AnUnknownFallbackScriptPolicyIsRejected() {
        Assert.That(
            () => PageContentTypes.Create(
                new PageContentTypeRule(Html, (PageScriptPolicy)42),
                []),
            Throws.ArgumentException);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankRuleMediaTypeIsRejected(string mediaType) {
        Assert.That(
            () => PageContentTypes.Create(
                new PageContentTypeRule(Html),
                [new KeyValuePair<string, PageContentTypeRule>(".js", new PageContentTypeRule(mediaType))]),
            Throws.ArgumentException);
    }

    [Test]
    public void ANullRuleIsRejected() {
        Assert.That(
            () => PageContentTypes.Create(
                new PageContentTypeRule(Html),
                [new KeyValuePair<string, PageContentTypeRule>(".js", null!)]),
            Throws.ArgumentException);
    }

    private static PageContentTypes Shipped() => PageContentTypes.Create(Html, new Dictionary<string, string?> {
        [".html"] = Html,
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    });
}
