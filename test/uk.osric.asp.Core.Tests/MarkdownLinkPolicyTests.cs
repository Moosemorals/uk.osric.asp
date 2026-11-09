// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Markdown;

[TestFixture]
public sealed class MarkdownLinkPolicyTests {

    [TestCase("/about")]
    [TestCase("/about?tab=one#top")]
    [TestCase("#top")]
    [TestCase("?q=one")]
    [TestCase("./about")]
    [TestCase("../about")]
    [TestCase("about")]
    [TestCase("http://example.com")]
    [TestCase("https://example.com/path?q=1#top")]
    public void ASameSiteOrHttpUrlMayBecomeALink(string url) {
        Assert.That(LinkPolicy.IsAllowed(url), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("//example.com")]
    [TestCase("\\\\example.com")]
    [TestCase("jav\tascript:alert(1)")]
    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/html,<script>alert(1)</script>")]
    [TestCase("mailto:owner@example.com")]
    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/file")]
    public void AnUnsafeOrUnknownUrlMayNotBecomeALink(string? url) {
        Assert.That(LinkPolicy.IsAllowed(url), Is.False);
    }
}
