// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageObjectTypesTests {

    [TestCase("image/png")]
    [TestCase("image/jpeg")]
    [TestCase("image/gif")]
    [TestCase("image/webp")]
    [TestCase("text/plain; charset=utf-8")]
    [TestCase("application/octet-stream")]
    public void AnAllowedContentTypeIsStoredAsItsCanonicalSpelling(string contentType) {
        Assert.That(PageObjectTypes.Canonical(contentType), Is.EqualTo(contentType));
    }

    [TestCase("IMAGE/PNG", "image/png")]
    [TestCase(" text/plain;charset=UTF-8 ", "text/plain; charset=utf-8")]
    [TestCase("Text/Plain ; Charset = Utf-8", "text/plain; charset=utf-8")]
    public void CaseAndWhitespaceAreTolerated(string given, string canonical) {
        Assert.That(PageObjectTypes.Canonical(given), Is.EqualTo(canonical));
    }

    [TestCase("image/svg+xml")]
    [TestCase("text/html")]
    [TestCase("application/json")]
    [TestCase("")]
    [TestCase("not a content type")]
    public void AMarkupOrUnknownTypeIsRefused(string contentType) {
        Assert.Multiple(() => {
            Assert.That(PageObjectTypes.IsAllowed(contentType), Is.False);
            Assert.That(
                () => PageObjectTypes.Canonical(contentType),
                Throws.ArgumentException.With.Message.Contains("is not a content type the object store serves"));
        });
    }
}
