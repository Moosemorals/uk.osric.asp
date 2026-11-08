// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Text;

[TestFixture]
public sealed class LowercaseTextTests {

    [TestCase("HELLO", "hello")]
    [TestCase("/Hello/World", "/hello/world")]
    [TestCase("osric", "osric")]
    [TestCase("", "")]
    public void TextIsLowercased(string input, string expected) {

        Assert.That(LowercaseText.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void ComposedAndDecomposedTextGiveTheSameValue() {

        string composed = LowercaseText.Normalize("CAF\u00c9");
        string decomposed = LowercaseText.Normalize("CAFE\u0301");

        Assert.That(decomposed, Is.EqualTo(composed));
        Assert.That(decomposed, Is.EqualTo("caf\u00e9"));
    }

    [Test]
    public void TheResultIsInNfc() {

        string normalized = LowercaseText.Normalize("Cafe\u0301");

        Assert.That(normalized.Normalize(), Is.EqualTo(normalized));
    }

    [Test]
    public void CombiningMarksAfterTheFoldAreStillComposed() {

        // The accent arrives after a lowercased base, so composing last is what makes this "å" rather
        // than an unconverted "a" plus a ring.
        Assert.That(LowercaseText.Normalize("A\u030a"), Is.EqualTo("\u00e5"));
    }

    [Test]
    [SetCulture("tr-TR")]
    public void CaseMappingIsCultureIndependent() {

        Assert.That(LowercaseText.Normalize("I"), Is.EqualTo("i"));
    }

    [Test]
    public void NullIsRejected() {

        _ = Assert.Throws<ArgumentNullException>(() => LowercaseText.Normalize(null!));
    }
}
