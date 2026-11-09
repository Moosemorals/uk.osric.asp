// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can reach Markdown through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageMarkdownRenderTests {

    private const string MarkdownPage = """
        <% IMarkdownRenderer markdown = ctx.Features.GetRequired<IMarkdownRenderer>(); %>
        <%== markdown.ToHtml("## Heading\n\n**bold** and [about](/about)\n\n| Name | Value |\n| --- | --- |\n| one | 1 |\n\n<span>raw</span>") %>
        """;

    [Test]
    public async Task APageRendersMarkdownThroughTheCapabilityAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/markdown", MarkdownPage);

        (RenderResult result, string output) = await pages.RenderAsync("/markdown");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.Multiple(() => {
            Assert.That(output, Does.Contain("<h2>Heading</h2>"));
            Assert.That(output, Does.Contain("<strong>bold</strong>"));
            Assert.That(output, Does.Contain("<a href=\"/about\">about</a>"));
            Assert.That(output, Does.Contain("<th scope=\"col\">Name</th>"));
            Assert.That(output, Does.Contain("<td>one</td>"));
            Assert.That(output, Does.Not.Contain("style="), "a table may not carry an inline style under the CSP");
            Assert.That(output, Does.Contain("<span>raw</span>"), "the page renderer passes raw HTML through");
        });
    }
}
