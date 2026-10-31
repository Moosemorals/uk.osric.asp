// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class ContentFingerprintTests {

    [TestCase("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [TestCase("hello", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    [TestCase("\u00e9", "4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c")]
    [TestCase("line one\nline two: \u00e9", "8233a0cbbf07d2e9d605e7a345d3bdca90cde191fa4d495c5cbc1b9c02ac58c1")]
    public void SourceIsHashedAsUtf8(string content, string expected) {
        Assert.That(ContentFingerprint.Of(content), Is.EqualTo(expected));
    }

    [Test]
    public void FingerprintIsSixtyFourLowercaseHexCharacters() {
        string fingerprint = ContentFingerprint.Of("Hello, world!");

        Assert.That(fingerprint, Has.Length.EqualTo(64));
        Assert.That(fingerprint, Does.Match("^[0-9a-f]{64}$"));
    }

    [Test]
    public void IdenticalContentProducesTheSameFingerprint() {
        string first = ContentFingerprint.Of("Hello.");
        string second = ContentFingerprint.Of("Hello.");

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void ADifferentContentProducesADifferentFingerprint() {
        Assert.That(ContentFingerprint.Of("Hello."), Is.Not.EqualTo(ContentFingerprint.Of("Hello!")));
    }

    [Test]
    public void NullContentIsRejected() {
        Assert.That(() => ContentFingerprint.Of(null!), Throws.ArgumentNullException);
    }
}
