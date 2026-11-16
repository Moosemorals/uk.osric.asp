// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageResponseStateTests {

    [Test]
    public void ResponseDefaultsToOkHtml() {
        PageResponseState response = CreateState();

        PageResponse built = response.ToResponse();

        Assert.Multiple(() => {
            Assert.That(built.Status, Is.EqualTo(200));
            Assert.That(built.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(built.Headers["Cache-Control"], Is.EqualTo("no-store"));
            Assert.That(built.Location, Is.Null);
            Assert.That(built.AllowsScripts, Is.False);
        });
    }

    [TestCase(99)]
    [TestCase(600)]
    public void StatusOutsideTheRangeIsRefused(int status) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetStatus(status), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(response.ToResponse().Status, Is.EqualTo(200));
    }

    [Test]
    public void AllowScriptsIsRecordedInTheResponse() {
        PageResponseState response = CreateState();

        response.AllowScripts();

        Assert.That(response.ToResponse().AllowsScripts, Is.True);
    }

    [Test]
    public void AllowScriptsIsIdempotent() {
        PageResponseState response = CreateState();

        response.AllowScripts();
        response.AllowScripts();

        Assert.That(response.ToResponse().AllowsScripts, Is.True);
    }

    [TestCase("Content-Language")]
    [TestCase("content-language")]
    [TestCase("X-Robots-Tag")]
    public void AllowedHeadersAreRecorded(string name) {
        PageResponseState response = CreateState();

        response.SetHeader(name, "value");

        Assert.That(response.ToResponse().Headers[name], Is.EqualTo("value"));
    }

    [TestCase("Set-Cookie")]
    [TestCase("Content-Length")]
    [TestCase("Content-Type")]
    [TestCase("Location")]
    [TestCase("Transfer-Encoding")]
    [TestCase("Connection")]
    [TestCase("ETag")]
    [TestCase("Expires")]
    [TestCase("Vary")]
    public void ReservedHeadersAreRefused(string name) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetHeader(name, "value"), Throws.ArgumentException);
        Assert.That(response.ToResponse().Headers.ContainsKey(name), Is.False);
    }

    // A stored page is per-visitor, so caching policy is the host's and a page cannot claim one.
    [TestCase("Cache-Control")]
    [TestCase("cache-control")]
    public void ACachePolicyThePageSetsIsRefused(string name) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetHeader(name, "max-age=3600"), Throws.ArgumentException);
        Assert.That(response.ToResponse().Headers["Cache-Control"], Is.EqualTo("no-store"));
    }

    [TestCase("X-Content-Type-Options")]
    [TestCase("x-content-type-options")]
    [TestCase("X-Frame-Options")]
    [TestCase("X-XSS-Protection")]
    public void TheShellsOwnXHeadersAreRefusedThroughTheWildcard(string name) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetHeader(name, ""), Throws.ArgumentException);
        Assert.That(() => response.SetHeader(name, "value"), Throws.ArgumentException);
        Assert.That(response.ToResponse().Headers.ContainsKey(name), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyHeaderValuesAreRefused(string value) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetHeader("X-Page", value), Throws.ArgumentException);
        Assert.That(response.ToResponse().Headers.ContainsKey("X-Page"), Is.False);
    }

    [TestCase("value\r\nSet-Cookie: stolen=yes")]
    [TestCase("value\n")]
    public void HeaderValuesWithLineBreaksAreRefused(string value) {
        PageResponseState response = CreateState();

        Assert.That(() => response.SetHeader("X-Page", value), Throws.ArgumentException);
    }

    [Test]
    public void RedirectRecordsTheLocationAndTheStatus() {
        PageResponseState response = CreateState();

        response.Redirect("/elsewhere");

        Assert.That(response.ToResponse().Location, Is.EqualTo("/elsewhere"));
        Assert.That(response.ToResponse().Status, Is.EqualTo(302));
    }

    [Test]
    public void RedirectKeepsAnExplicitStatus() {
        PageResponseState response = CreateState();

        response.Redirect("/moved", 301);

        Assert.That(response.ToResponse().Status, Is.EqualTo(301));
    }

    [TestCase("value\r\nSet-Cookie: stolen=yes")]
    [TestCase("value\n")]
    public void RedirectTargetsWithLineBreaksAreRefused(string location) {
        PageResponseState response = CreateState();

        Assert.That(() => response.Redirect(location), Throws.ArgumentException);
        Assert.That(response.ToResponse().Location, Is.Null);
    }

    private static PageResponseState CreateState(string contentType = "text/html; charset=utf-8") {
        return new PageResponseState(contentType);
    }
}
