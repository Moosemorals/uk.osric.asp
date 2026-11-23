// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text;
using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageObjectHashTests {

    [Test]
    public void TheDigestIsTheLowercaseSha256OfTheBytes() {
        // The well-known SHA-256 of the empty string.
        Assert.That(
            PageObjectHash.Of([]),
            Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
    }

    [Test]
    public void TheSameBytesHaveTheSameDigest() {
        byte[] first = Encoding.UTF8.GetBytes("the same bytes");
        byte[] second = Encoding.UTF8.GetBytes("the same bytes");

        Assert.That(PageObjectHash.Of(first), Is.EqualTo(PageObjectHash.Of(second)));
    }

    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", true)]
    [TestCase("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", false)]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85", false)]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b8550", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void OnlyTheLowercaseSixtyFourCharacterSpellingIsCanonical(string? sha, bool canonical) {
        Assert.That(PageObjectHash.IsCanonical(sha), Is.EqualTo(canonical));
    }
}
