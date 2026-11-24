// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class LineSnippetTests {

    [Test]
    public void AWindowIsCentredOnTheMatch() {
        const string line = "0123456789abcdefghijklmnopqrstuvwxyz";

        LineSnippet snippet = LineSnippet.Of(line, index: 20, length: 4, context: 10);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo("hijklmnopq"));
            Assert.That(snippet.Start, Is.EqualTo(17));
            Assert.That(snippet.Clipped, Is.True);
            Assert.That(snippet.Text[(20 - snippet.Start)..], Does.StartWith("klmn"), "the match is where the caller expects it");
        });
    }

    [Test]
    public void AWindowIsCountedInUtf8BytesAndNeverSplitsACharacter() {
        // Five three-byte characters: a ten-byte window holds the match and two more, and nothing is
        // ever cut in half.
        LineSnippet snippet = LineSnippet.Of("水水水水水", index: 0, length: 1, context: 10);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo("水水水"));
            Assert.That(snippet.Text, Does.Not.Contain("\uFFFD"));
            Assert.That(snippet.Clipped, Is.True);
        });
    }

    [Test]
    public void AWindowAtTheStartOfTheLineIsPushedInsideIt() {
        LineSnippet snippet = LineSnippet.Of("0123456789abcdef", index: 1, length: 3, context: 10);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo("0123456789"));
            Assert.That(snippet.Start, Is.Zero);
            Assert.That(snippet.Clipped, Is.True, "the line goes on after the window");
        });
    }

    [Test]
    public void AWindowAtTheEndOfTheLineIsPushedInsideIt() {
        LineSnippet snippet = LineSnippet.Of("0123456789abcdef", index: 13, length: 3, context: 10);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo("6789abcdef"));
            Assert.That(snippet.Start, Is.EqualTo(6));
            Assert.That(snippet.Clipped, Is.True);
        });
    }

    [Test]
    public void AMatchLongerThanTheContextIsAnsweredWhole() {
        const string line = "0123456789abcdefghij";

        LineSnippet snippet = LineSnippet.Of(line, index: 5, length: 12, context: 8);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo(line[5..17]));
            Assert.That(snippet.Start, Is.EqualTo(5));
            Assert.That(snippet.Clipped, Is.True, "the line goes on either side");
        });
    }

    [Test]
    public void ALineShorterThanTheContextIsAnsweredWhole() {
        LineSnippet snippet = LineSnippet.Of("short", index: 1, length: 2, context: 64);

        Assert.Multiple(() => {
            Assert.That(snippet.Text, Is.EqualTo("short"));
            Assert.That(snippet.Start, Is.Zero);
            Assert.That(snippet.Clipped, Is.False);
        });
    }

    [Test]
    public void AMatchOutsideTheLineIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => LineSnippet.Of("abc", index: 2, length: 4, context: 8), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => LineSnippet.Of("abc", index: 0, length: 1, context: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => LineSnippet.Of(null!, index: 0, length: 0, context: 8), Throws.ArgumentNullException);
        });
    }
}
