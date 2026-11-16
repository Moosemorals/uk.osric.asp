// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;

[TestFixture]
public sealed class MarkdownRendererTests {

    private const string Table = """
        | Name | Value |
        | --- | --- |
        | one | 1 |
        """;

    private readonly MarkdownRenderer _renderer = new();

    [Test]
    public void ThePageProfileRendersCommonMarkAndRawHtml() {
        string html = _renderer.ToHtml("""
            ## Heading

            **bold** and *italic* with `code`.

            - one
            - two

            <span>raw</span>
            """);

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<h2 id=\"heading\">Heading</h2>"));
            Assert.That(html, Does.Contain("<strong>bold</strong>"));
            Assert.That(html, Does.Contain("<em>italic</em>"));
            Assert.That(html, Does.Contain("<code>code</code>"));
            Assert.That(html, Does.Contain("<li>one</li>"));
            Assert.That(html, Does.Contain("<span>raw</span>"), "page Markdown passes raw HTML through");
        });
    }

    [Test]
    public void ThePageProfileGivesAHeadingAnAnchor() {
        string html = _renderer.ToHtml("# One\n\n## One");

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<h1 id=\"one\">One</h1>"));
            Assert.That(html, Does.Contain("<h2 id=\"one-1\">One</h2>"), "a repeat gets its own anchor");
        });
    }

    [Test]
    public void AHeadingAnchorCanBeTurnedOff() {
        string html = _renderer.ToHtml("## Heading", new MarkdownOptions { HeadingAnchors = false });

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<h2>Heading</h2>"));
            Assert.That(html, Does.Not.Contain("id="));
        });
    }

    [Test]
    public void AutoLinksCanBeTurnedOff() {
        string html = _renderer.ToHtml("https://example.com", new MarkdownOptions { AutoLinks = false });

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<a "));
            Assert.That(html, Does.Contain("https://example.com"));
        });
    }

    [Test]
    public void PipeTablesCanBeTurnedOff() {
        string html = _renderer.ToHtml(Table, new MarkdownOptions { PipeTables = false });

        Assert.That(html, Does.Not.Contain("<table>"));
    }

    [Test]
    public void TheDefaultOptionsAreThePageProfile() {
        string html = _renderer.ToHtml("## Heading\n\n| Name | Value |\n| --- | --- |\n| one | 1 |\n\nhttps://example.com",
            MarkdownOptions.Default);

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<h2 id=\"heading\">Heading</h2>"));
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Contain("<a href=\"https://example.com\">https://example.com</a>"));
        });
    }

    [Test]
    public void ThePageProfileLinksAnHttpUrl() {
        string html = _renderer.ToHtml("[example](https://example.com)");

        Assert.That(html, Does.Contain("<a href=\"https://example.com\">example</a>"));
    }

    [Test]
    public void BothProfilesRenderAPipeTableWithScopedHeadersAndNoInlineStyle() {
        string page = _renderer.ToHtml(Table);
        string restricted = MarkdownRenderer.ToRestrictedHtml(Table);

        Assert.Multiple(() => {
            Assert.That(page, Does.Contain("<table>"));
            Assert.That(page, Does.Contain("<th scope=\"col\">Name</th>"));
            Assert.That(page, Does.Contain("<td>one</td>"));
            Assert.That(page, Does.Not.Contain("style="));
            Assert.That(restricted, Does.Contain("<table>"));
            Assert.That(restricted, Does.Contain("<th scope=\"col\">Name</th>"));
            Assert.That(restricted, Does.Contain("<td>one</td>"));
            Assert.That(restricted, Does.Not.Contain("style="));
        });
    }

    [Test]
    public void TheRestrictedProfileTreatsRawHtmlAsText() {
        string html = MarkdownRenderer.ToRestrictedHtml("<script>alert(1)</script>");

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Contain("&lt;script&gt;"));
        });
    }

    [Test]
    public void TheRestrictedProfileDropsAnImageToItsAltText() {
        string html = MarkdownRenderer.ToRestrictedHtml("![a picture](/picture.png)");

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<img"));
            Assert.That(html, Does.Contain("a picture"));
        });
    }

    [Test]
    public void TheRestrictedProfileKeepsAnAllowedLinkAndItsText() {
        string html = MarkdownRenderer.ToRestrictedHtml(
            "[off site](https://example.com) [on site](/about) [anchor](#top)");

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain(
                "<a href=\"https://example.com\" rel=\"nofollow noopener noreferrer\">off site</a>"));
            Assert.That(html, Does.Contain("<a href=\"/about\" rel=\"nofollow noopener noreferrer\">on site</a>"));
            Assert.That(html, Does.Contain("<a href=\"#top\" rel=\"nofollow noopener noreferrer\">anchor</a>"));
        });
    }

    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/html,hi")]
    [TestCase("mailto:owner@example.com")]
    [TestCase("//example.com")]
    public void TheRestrictedProfileRefusesAnUnsafeLinkAndKeepsItsText(string url) {
        string html = MarkdownRenderer.ToRestrictedHtml($"[click]({url})");

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<a "));
            Assert.That(html, Does.Contain("click"));
        });
    }

    [Test]
    public void TheRestrictedProfileFlattensHeadingsToH3() {
        string html = MarkdownRenderer.ToRestrictedHtml("# One\n\n#### Four");

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<h3>One</h3>"));
            Assert.That(html, Does.Contain("<h3>Four</h3>"));
            Assert.That(html, Does.Not.Contain("<h1>"));
            Assert.That(html, Does.Not.Contain("<h4>"));
        });
    }

    [Test]
    public void TheRestrictedProfileDoesNotAutoLinkABareUrl() {
        string html = MarkdownRenderer.ToRestrictedHtml("https://example.com");

        Assert.That(html, Does.Not.Contain("<a "));
        Assert.That(html, Does.Contain("https://example.com"));
    }

    [Test]
    public void TheRestrictedProfileEscapesAFencedCodeBlock() {
        string html = MarkdownRenderer.ToRestrictedHtml("```html\n<script>alert(1)</script>\n```");

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Contain("&lt;script&gt;"));
        });
    }

    [Test]
    public void ANullDocumentIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => _renderer.ToHtml(null!), Throws.ArgumentNullException);
            Assert.That(() => MarkdownRenderer.ToRestrictedHtml(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void NullOptionsAreRefused() {
        Assert.That(() => _renderer.ToHtml("text", null!), Throws.ArgumentNullException);
    }
}
