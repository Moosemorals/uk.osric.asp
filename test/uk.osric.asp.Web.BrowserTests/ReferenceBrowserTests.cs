// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>The reference's external-link mark, which no string test can see.</summary>
[TestFixture]
internal sealed class ReferenceBrowserTests : BrowserTest {

    [Test]
    public async Task ALinkThatLeavesTheSiteCarriesTheArrowAndACorpusLinkDoesNotAsync() {
        await Page.GotoAsync("admin/reference/sdk");

        string external = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.querySelector('a[href^=\"http\"]'), '::after').content");
        string internalLink = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.querySelector('.type-index a[href^=\"#\"]'), '::after').content");

        Assert.Multiple(() => {
            Assert.That(external, Does.Contain("\u27b8"));
            Assert.That(internalLink, Is.EqualTo("none"));
        });
    }

    [Test]
    public async Task AMemberLinkOpensTheTypeThatHoldsItAsync() {
        await Page.GotoAsync("admin/reference/sdk#m-IContext.Request");

        bool open = await Page.EvaluateAsync<bool>(
            "document.getElementById('t-IContext').open");

        Assert.That(open, Is.True, "a member inside a closed type must be revealed by its link");
    }
}
