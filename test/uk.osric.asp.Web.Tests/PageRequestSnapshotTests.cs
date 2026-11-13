// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using uk.osric.asp.Sdk;
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

    [Test]
    public void ARepeatedQueryValueKeepsEveryValueAndCollapsesToTheFirstValue() {
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString("?tag=a&tag=b");

        PageRequest request = PageRequestSnapshot.Create(context.Request);
        string[] expected = ["a", "b"];

        Assert.Multiple(() => {
            Assert.That(request.Query["tag"], Is.EqualTo("a"), "the collapsed name keeps the first value");
            Assert.That(request.QueryValues["tag"], Is.EqualTo(expected), "every value, in order");
        });
    }

    [Test]
    public void ARepeatedFormValueKeepsEveryValueAndCollapsesToTheFirstValue() {
        DefaultHttpContext context = new();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["tag"] = new(["a", "b"]),
        });

        PageRequest request = PageRequestSnapshot.Create(context.Request);
        string[] expected = ["a", "b"];

        Assert.Multiple(() => {
            Assert.That(request.Form["tag"], Is.EqualTo("a"));
            Assert.That(request.FormValues["tag"], Is.EqualTo(expected));
        });
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
