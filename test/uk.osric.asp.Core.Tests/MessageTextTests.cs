// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using System.Globalization;
using uk.osric.asp.Core.Text;

[TestFixture]
public sealed class MessageTextTests {

    [Test]
    public void TextWithinTheLimitIsUnchanged() {

        const string text = "edit_page";

        Assert.That(MessageText.Sanitize(text), Is.EqualTo(text));
    }

    [Test]
    public void TextAtTheLimitIsUnchanged() {

        string text = new('a', MessageText.MaximumLength);

        Assert.That(MessageText.Sanitize(text), Is.EqualTo(text));
    }

    [Test]
    public void TextOverTheLimitEndsWithAnEllipsisWithinTheLimit() {

        string sanitized = MessageText.Sanitize(new string('a', MessageText.MaximumLength + 1));

        Assert.That(sanitized, Is.EqualTo(new string('a', MessageText.MaximumLength - 1) + MessageText.Ellipsis));
        Assert.That(sanitized, Has.Length.EqualTo(MessageText.MaximumLength));
    }

    [Test]
    public void TheEllipsisIsOneCharacter() {

        Assert.That(MessageText.Ellipsis, Has.Length.EqualTo(1));
        Assert.That(MessageText.Ellipsis, Is.EqualTo("\u2026"));
    }

    [Test]
    public void LineBreaksAndTabsCollapseToSingleSpaces() {

        Assert.That(MessageText.Sanitize("  first\r\nsecond\t\tthird\n\nfourth  "), Is.EqualTo("first second third fourth"));
    }

    [Test]
    public void ControlCharactersAreDropped() {

        Assert.That(MessageText.Sanitize("a\u0000b\u0007c\u001bd"), Is.EqualTo("abcd"));
    }

    [Test]
    public void BidiAndZeroWidthFormattingIsDropped() {

        // A right-to-left override and a zero-width space would let the value read as something else.
        Assert.That(MessageText.Sanitize("\u202eabc\u200b\ufeffdef"), Is.EqualTo("abcdef"));
    }

    [Test]
    public void AnUnpairedSurrogateIsDroppedSoTheResultIsValidUnicode() {

        string sanitized = MessageText.Sanitize("a\ud800b");

        Assert.That(sanitized, Is.EqualTo("ab"));
        Assert.That(sanitized.Normalize(), Is.EqualTo(sanitized));
    }

    [Test]
    public void ASurrogatePairIsNeverCutInHalf() {

        string sanitized = MessageText.Sanitize(string.Concat(Enumerable.Repeat("\U0001f600", MessageText.MaximumLength + 1)));

        Assert.That(sanitized[^1], Is.EqualTo(MessageText.Ellipsis[0]));
        Assert.That(sanitized, Does.EndWith("\U0001f600" + MessageText.Ellipsis));
        Assert.That(char.IsHighSurrogate(sanitized[^3]), Is.True);
        Assert.That(char.IsLowSurrogate(sanitized[^2]), Is.True);
    }

    [Test]
    public void ACombiningMarkStaysWithItsBaseCharacter() {

        string text = string.Concat(Enumerable.Repeat("e\u0301", MessageText.MaximumLength + 1));

        string sanitized = MessageText.Sanitize(text);

        Assert.That(sanitized, Does.EndWith("e\u0301" + MessageText.Ellipsis));
        Assert.That(char.GetUnicodeCategory(sanitized[^3]), Is.EqualTo(UnicodeCategory.LowercaseLetter));
    }

    [Test]
    public void WhitespaceOnlyTextBecomesEmpty() {

        Assert.That(MessageText.Sanitize(" \r\n\t "), Is.Empty);
    }

    [Test]
    public void NullIsRejected() {

        _ = Assert.Throws<ArgumentNullException>(() => MessageText.Sanitize(null!));
    }
}
