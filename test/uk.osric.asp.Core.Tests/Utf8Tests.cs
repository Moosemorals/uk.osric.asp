// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Text;

[TestFixture]
public sealed class Utf8Tests {

    [TestCase("", 0)]
    [TestCase("abc", 3)]
    [TestCase("café", 5)]
    [TestCase("水", 3)]
    [TestCase("😀", 4)]
    public void TextIsCountedInItsUtf8Bytes(string text, int expected) =>
        Assert.That(Utf8.SizeInBytes(text), Is.EqualTo(expected));

    [Test]
    public void ALoneSurrogateCountsAsItsReplacement() {
        const string text = "\uD800";

        Assert.Multiple(() => {
            Assert.That(text.Length, Is.EqualTo(1));
            Assert.That(Utf8.SizeInBytes(text), Is.EqualTo(3));
        });
    }

    [Test]
    public void AClipStopsBeforeTheBudgetIsExceeded() =>
        Assert.That(Utf8.Clip("abcdef", 4), Is.EqualTo("abcd"));

    [TestCase("éé", 3, "é")]
    [TestCase("水水", 4, "水")]
    [TestCase("😀😀", 6, "😀")]
    public void AClipNeverSplitsACodePoint(string text, int budget, string expected) =>
        Assert.That(Utf8.Clip(text, budget), Is.EqualTo(expected));

    [Test]
    public void AClipOfEverythingIsTheTextItself() {
        const string text = "a page in bytes";

        Assert.That(Utf8.Clip(text, Utf8.SizeInBytes(text)), Is.SameAs(text));
    }

    [Test]
    public void AClipThatFitsIsTheTextItself() {
        const string text = "short";

        Assert.That(Utf8.Clip(text, 100), Is.SameAs(text));
    }

    [Test]
    public void AZeroBudgetClipsEverything() =>
        Assert.That(Utf8.Clip("anything", 0), Is.Empty);

    [Test]
    public void ALoneSurrogateIsKeptWhenItsReplacementBytesFit() =>
        Assert.That(Utf8.Clip("\uD800a", 3), Is.EqualTo("\uD800"));

    [Test]
    public void ANegativeBudgetIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf8.Clip("text", -1));

    [TestCase("", 1)]
    [TestCase("plain", 3)]
    [TestCase("é", 1)]
    [TestCase("水", 1)]
    [TestCase("😀", 2)]
    [TestCase("\uD800", 1)]
    [TestCase("\uDC00", 1)]
    public void AClipFitsEveryBudgetAndIsAPrefix(string text, int budget) {
        string clipped = Utf8.Clip(text, budget);

        Assert.Multiple(() => {
            Assert.That(Utf8.SizeInBytes(clipped), Is.LessThanOrEqualTo(budget));
            Assert.That(text, Does.StartWith(clipped));
        });
    }

    [Test]
    public void APairOfSurrogatesIsTwoCodeUnits() =>
        Assert.That(Utf8.NextCodePointUnits("😀", 0), Is.EqualTo(2));

    [Test]
    public void ALoneSurrogateIsOneCodeUnit() =>
        Assert.That(Utf8.NextCodePointUnits("a\uD800", 1), Is.EqualTo(1));

    [Test]
    public void AnIndexOutsideTheTextIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Utf8.NextCodePointUnits("text", 4));
}
