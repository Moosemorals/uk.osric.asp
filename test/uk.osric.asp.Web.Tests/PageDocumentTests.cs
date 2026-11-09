// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

/// <summary>The document policy the builder owns, asserted once for both callers.</summary>
/// <remarks>
/// The Sdk has no suite of its own, so the builder is tested from here where the rest of the markup
/// is. A page and a shell view both write through it, so these rules are what stops them diverging.
/// </remarks>
[TestFixture]
public sealed class PageDocumentTests {

    private static readonly string[] s_baseSheets = ["/layout.css", "/site.css"];

    [Test]
    public void TheHeadMatchesWhatTheShellWrites() {
        string head = PageDocument.Head("Title", ["/editor.css"], head: "<meta name=\"refresh\" content=\"5\">", bodyClass: "editor-page");

        MarkupReader document = MarkupReader.Parse(head + PageDocument.OpenMain() + PageDocument.CloseMain() + PageDocument.End());
        Assert.That(document.Elements("html").Single().Attribute("lang"), Is.EqualTo("en"));
        Assert.That(document.Elements("head"), Has.Count.EqualTo(1));
        Assert.That(document.Elements("body").Single().Attribute("class"), Is.EqualTo("editor-page"));
        Assert.That(document.Elements("meta").Select(meta => meta.Attribute("name")), Does.Contain("refresh"));
        Assert.That(document.Elements("link").Any(link => link.Attribute("rel") == "icon"), Is.True);
    }

    [Test]
    public void TheSheetsAreLinkedLayoutThenSiteThenTheView() {
        Assert.That(Sheets(PageDocument.Head("Title", ["/editor.css"])),
            Is.EqualTo((string[])["/layout.css", "/site.css", "/editor.css"]));
    }

    [Test]
    public void APageWithNoSheetsGetsTheBaseTwoAndNothingElse() {
        Assert.That(Sheets(PageDocument.Head("Title")), Is.EqualTo(s_baseSheets));
    }

    [Test]
    public void EverySheetThePageAsksForIsLinkedInTheOrderItAsked() {
        Assert.That(Sheets(PageDocument.Head("Title", ["/blog.css", "/print.css"])),
            Is.EqualTo((string[])["/layout.css", "/site.css", "/blog.css", "/print.css"]));
    }

    [Test]
    public void OnePageMayLinkSeveralSheets() {
        string[] sheets = Sheets(PageDocument.Head("Title", ["/one.css", "/two.css", "/three.css"]));

        Assert.That(sheets, Has.Length.EqualTo(5));
    }

    [Test]
    public void TheSameSheetIsLinkedOnlyOnce() {
        Assert.That(Sheets(PageDocument.Head("Title", ["/site.css", "/page.css", "/page.css"])),
            Is.EqualTo((string[])["/layout.css", "/site.css", "/page.css"]));
    }

    [TestCase("https://example.com/site.css")]
    [TestCase("//example.com/site.css")]
    [TestCase("site.css")]
    [TestCase("/\\example.com/site.css")]
    [TestCase("")]
    public void ASheetThatIsNotSiteRelativeIsRefused(string href) {
        Assert.Throws<ArgumentException>(() => PageDocument.Head("Title", [href]));
    }

    [Test]
    public void TheBannerIsTheSkipLinkThenTheHeaderAroundTheNav() {
        string nav = PageDocument.Nav("Site", [new("/", "Home")]);

        MarkupReader document = MarkupReader.Parse(PageDocument.Banner(nav));

        Assert.That(document.Root.Children[0].Name, Is.EqualTo("a"));
        Assert.That(document.Root.Children[0].Attribute("href"), Is.EqualTo("#main"));
        Assert.That(document.Root.Children[1].Name, Is.EqualTo("header"));
        Assert.That(document.Root.Children[1].Children.Single().Name, Is.EqualTo("nav"));
    }

    [Test]
    public void TheOpenBodyAndTheMainLandmarkAreSeparatedSoChromeCanGoBetween() {
        string head = PageDocument.Head("Title");
        string open = PageDocument.OpenMain();

        Assert.That(head.TrimEnd(), Does.EndWith("<body>"));
        Assert.That(open, Does.StartWith("<main"));
        Assert.That(head, Does.Not.Contain("id=\"main\""));
    }

    [Test]
    public void ClosingTheDocumentClosesTheLandmarkItOpened() {
        MarkupReader document = MarkupReader.Parse(
            PageDocument.Head("Title") + PageDocument.OpenMain() + "<h1>Body</h1>" + PageDocument.CloseMain() + PageDocument.End());

        MarkupElement main = document.Elements("main").Single();
        Assert.That(main.Attribute("id"), Is.EqualTo("main"));
        Assert.That(main.Children.Single().Name, Is.EqualTo("h1"));
        Assert.That(document.Elements("body").Single().Children.Single().Name, Is.EqualTo("main"));
    }

    [Test]
    public void TheBuilderEncodesTheTitleAndTheAttributes() {
        string head = PageDocument.Head("<script>alert('t')</script>", ["/a.css?x=1&amp;y=2"], bodyClass: "\"quoted\"");
        string nav = PageDocument.Nav("<Site>", [new("/a?b=<c>", "<Home>")], "<menu>");

        Assert.That(head, Does.Not.Contain("<script>alert"));
        Assert.That(head, Does.Contain("&lt;script&gt;"));
        Assert.That(head, Does.Contain("&quot;quoted&quot;"));
        Assert.That(nav, Does.Not.Contain("<Home>"));
        Assert.That(nav, Does.Contain("&lt;Home&gt;"));
        Assert.That(nav, Does.Contain("&lt;Site&gt;"));
        Assert.That(nav, Does.Contain("&lt;menu&gt;"));
    }

    [Test]
    public void NavRendersLinksAndMarksTheCurrentItemAsText() {
        MarkupReader document = MarkupReader.Parse(PageDocument.Nav("Site", [
            new("/", "Home"),
            new("/edit", "Pages", IsCurrent: true),
        ]));

        MarkupElement nav = document.Elements("nav").Single();
        Assert.That(nav.Children[0].Name, Is.EqualTo("a"));
        Assert.That(nav.Children[1].Name, Is.EqualTo("span"));
        Assert.That(nav.Children[1].Attribute("aria-current"), Is.EqualTo("true"));
        Assert.That(nav.Children[1].HasAttribute("href"), Is.False);
    }

    [Test]
    public void NavEncodesTheHrefAndTheText() {
        string nav = PageDocument.Nav("Site", [new("/a?b=1&c=2", "A & B")]);

        Assert.That(nav, Does.Contain("href=\"/a?b=1&amp;c=2\""));
        Assert.That(nav, Does.Contain(">A &amp; B<"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void NavNamesTheLandmarkAndRefusesAnEmptyName(string label) {
        string nav = PageDocument.Nav("Site", [new("/", "Home")]);

        Assert.That(nav, Does.Contain("aria-label=\"Site\""));
        Assert.Throws<ArgumentException>(() => PageDocument.Nav(label, [new("/", "Home")]));
    }

    [Test]
    public void ANavLinkWithoutHrefOrTextIsRefused() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => _ = new NavLink(null!, "Home"));
            Assert.Throws<ArgumentException>(() => _ = new NavLink("/", "  "));
        });
    }

    [Test]
    public void ARequestWithoutAUrlIsRefused() {
        Assert.Throws<ArgumentException>(() => _ = new HttpFetchRequest("  "));
    }

    [Test]
    public void NothingInTheBuilderWritesAHeaderOrAStatus() {
        string markup = string.Concat(
            PageDocument.Head("Title", ["/a.css"]),
            PageDocument.Banner(PageDocument.Nav("Site", [new("/", "Home")])),
            PageDocument.OpenMain(),
            PageDocument.CloseMain(),
            PageDocument.End());

        Assert.That(markup, Does.Not.Contain("Content-Security-Policy"));
        Assert.That(markup, Does.Not.Contain("Cache-Control"));
        Assert.That(markup, Does.Not.Contain("no-store"));
        Assert.That(markup, Does.Not.Contain("http-equiv"));
    }

    private static string[] Sheets(string head) =>
        [.. MarkupReader.Parse(head)
            .Elements("link")
            .Where(link => string.Equals(link.Attribute("rel"), "stylesheet", StringComparison.Ordinal))
            .Select(link => link.Attribute("href")!)];
}
