// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Authentication;

[TestFixture]
public sealed class LoginRedirectTests {

    [TestCase("/private", "", "/private")]
    [TestCase("/", "", "/")]
    [TestCase("/search", "?q=metrics", "/search?q=metrics")]
    [TestCase("/search", "?q=a&q=b", "/search?q=a&q=b")]
    [TestCase("/search", "?q=a%20b", "/search?q=a%20b")]
    [TestCase("/caf\u00e9", "", "/caf\u00e9")]
    public void TheReturnTargetKeepsTheCanonicalPathAndTheRawQuery(string path, string query, string expected) {
        string target = LoginRedirect.For(PagePath.Normalize(path), new QueryString(query));

        Assert.That(target, Does.StartWith("/login?returnUrl="));
        Assert.That(ReturnUrl(target), Is.EqualTo(expected));
    }

    [Test]
    public void TheReturnTargetIsLocalSoTheLoginFormAcceptsIt() {
        string target = LoginRedirect.For(PagePath.Normalize("/private"), new QueryString("?q=1"));

        Assert.That(LocalRedirect.IsSafe(ReturnUrl(target)), Is.True);
    }

    // Parsed rather than compared raw: the encoding of '/' and '?' is the framework's business.
    private static string ReturnUrl(string target) =>
        QueryHelpers.ParseQuery(new Uri("https://site.example" + target).Query)["returnUrl"].ToString();
}
