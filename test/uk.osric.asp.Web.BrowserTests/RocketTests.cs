// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

/// <summary>The site's icon is a stored page, and it has to survive the page content security policy.</summary>
[TestFixture]
internal sealed class RocketTests : BrowserTest {

    [Test]
    public async Task TheIconKeepsItsColoursUnderThePagePolicyAsync() {
        string? screenshot = Environment.GetEnvironmentVariable("ROCKET_SCREENSHOT");

        await Page.SetViewportSizeAsync(256, 256);
        await Page.GotoAsync("rocket.svg");
        if (!string.IsNullOrEmpty(screenshot)) {
            await Page.ScreenshotAsync(new() { Path = screenshot });
        }

        string hull = await Page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.hull') || document.querySelector('path')).fill");
        string window = await Page.EvaluateAsync<string>("getComputedStyle(document.querySelector('circle')).fill");
        string stop = await Page.EvaluateAsync<string>("getComputedStyle(document.querySelector('stop')).stopColor");

        Assert.Multiple(() => {
            Assert.That(hull, Does.StartWith("url("), "the hull is filled from its gradient");
            Assert.That(window, Does.StartWith("url("), "the cabin is filled from its gradient");
            Assert.That(stop, Is.Not.EqualTo("rgb(0, 0, 0)"), "the gradient stops are coloured");
        });
    }
}
