// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageRequestSnapshotTests {

    [Test]
    public void APageSeesItsOwnCookiesAsync() {
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = "theme=dark";

        Assert.That(PageRequestSnapshot.Create(context.Request).Cookies, Does.ContainKey("theme"));
    }

    [TestCase(ShellCookies.Authentication)]
    [TestCase(ShellCookies.Antiforgery)]
    [TestCase(ShellCookies.Session)]
    public void TheShellsOwnCookiesAreNotVisibleToAPage(string name) {
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = $"{name}=value";

        Assert.That(PageRequestSnapshot.Create(context.Request).Cookies, Is.Empty,
            "the shell's cookies are the shell's identity and one visitor's state");
    }
}
