// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class MemoryContentTests {

    [Test]
    public void TrimsAndCollapsesWhitespace() {
        MemoryContentResult result = MemoryContent.Normalise("  The owner writes\n\nBritish\tEnglish.  ", 1_000);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Error);
            Assert.That(result.Content, Is.EqualTo("The owner writes British English."));
        });
    }

    [Test]
    public void EmptyContentIsRefused() {
        Assert.That(MemoryContent.Normalise("   \n\t ", 1_000).IsSuccess, Is.False);
        Assert.That(MemoryContent.Normalise(null, 1_000).IsSuccess, Is.False);
    }

    [Test]
    public void ControlCharactersAreRefused() {
        MemoryContentResult result = MemoryContent.Normalise("a\u0000b", 1_000);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Does.Contain("control characters"));
    }

    [Test]
    public void TheCapIsCountedInCharactersRatherThanBytes() {
        MemoryContentResult result = MemoryContent.Normalise(new string('é', 11), 10);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Does.Contain("10"));
    }

    [Test]
    public void ContentAtExactlyTheCapIsAccepted() {
        Assert.That(MemoryContent.Normalise(new string('é', 10), 10).IsSuccess, Is.True);
    }

    [Test]
    public void TheSameNoteWrittenTwiceComparesEqualWhateverItsCase() {
        MemoryContentResult first = MemoryContent.Normalise("Prefers  colour", 1_000);
        MemoryContentResult second = MemoryContent.Normalise("prefers colour", 1_000);

        Assert.That(MemoryContent.SameNote(first.Content, second.Content), Is.True);
    }
}
