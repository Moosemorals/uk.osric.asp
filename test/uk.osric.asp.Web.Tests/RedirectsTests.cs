// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using NUnit.Framework;

/// <summary>Covers how a redirect target becomes the <c>Location</c> header.</summary>
[TestFixture]
public sealed class RedirectsTests {

    [TestCase("/hello", "/hello")]
    [TestCase("/hello world", "/hello%20world")]
    [TestCase("/caf\u00e9", "/caf%C3%A9")]
    [TestCase("/hello%20world", "/hello%20world")]
    [TestCase("/hello?q=1", "/hello?q=1")]
    [TestCase("/hello?", "/hello?")]
    [TestCase("/hello?q=a%20b", "/hello?q=a%20b")]
    [TestCase("/hello#top", "/hello#top")]
    [TestCase("/hello?q=1#top", "/hello?q=1#top")]
    [TestCase("/hello#top?q=1", "/hello#top?q=1")]
    [TestCase("https://example.com/x?y=1", "https://example.com/x?y=1")]
    public void ARelativeTargetKeepsItsQueryAndFragment(string target, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        Redirects.To(context, StatusCodes.Status302Found, target);

        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
    }

    [Test]
    public void ToSendsTheStatusItIsGiven() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        Redirects.To(context, StatusCodes.Status307TemporaryRedirect, "/hello");

        Assert.That(context.Response.StatusCode, Is.EqualTo(307));
    }

    [Test]
    public void PermanentIsPermanent() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        Redirects.Permanent(context, "/hello?q=1");

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/hello?q=1"));
    }

    [Test]
    public void SeeOtherIsSeeOther() {
        DefaultHttpContext context = TestHttp.CreateContext(null);

        Redirects.SeeOther(context, "/edit/hello?tab=source");

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello?tab=source"));
    }
}
