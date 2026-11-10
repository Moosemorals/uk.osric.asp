// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can reach the diff renderer through the real compiler.</summary>
[TestFixture]
public sealed class PageDiffRenderTests {

    private const string DiffPage = """
        <% IDiffRenderer diff = ctx.Features.GetRequired<IDiffRenderer>(); %>
        <%== diff.ToHtml("one\n<script>before</script>", "one\n<script>after</script>") %>
        """;

    [Test]
    public async Task APageRendersADiffThroughTheCapabilityAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/diff", DiffPage);

        (RenderResult result, string output) = await pages.RenderAsync("/diff");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.Multiple(() => {
            Assert.That(output, Does.Contain("<table class=\"diff\">"));
            Assert.That(output, Does.Contain("<tr class=\"diff-removed\">"));
            Assert.That(output, Does.Contain("<tr class=\"diff-added\">"));
            Assert.That(output, Does.Contain("&lt;script&gt;before&lt;/script&gt;"));
            Assert.That(output, Does.Contain("&lt;script&gt;after&lt;/script&gt;"));
            Assert.That(output, Does.Not.Contain("<script>"));
        });
    }
}
