// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class PageRangeEditorTests {

    [Test]
    public void ReplacesInclusiveLineRangeAndPreservesUnchangedLineEndings() {
        PageRangeEditResult result = PageRangeEditor.Replace("one\r\ntwo\r\nthree", 2, 2, "replacement", 100);

        Assert.That(result.Content, Is.EqualTo("one\r\nreplacement\r\nthree"));
        Assert.That(result.Error, Is.Null);
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void RangeCountsPhysicalTemplateLinesAcrossHtmlAndTemplateDelimiters() {
        const string Source = "<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"value\" %>\r\n<% } %>";

        PageRangeEditResult result = PageRangeEditor.Replace(
            Source, 5, 5, "<%= \"changed\" %>", 200);

        Assert.That(result.Content, Is.EqualTo("<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"changed\" %>\r\n<% } %>"));
    }

    [Test]
    public void RejectsInvalidLineBounds() {
        Assert.Multiple(() => {
            Assert.That(PageRangeEditor.Replace("one", 0, 1, "x", 100).Error, Is.Not.Null);
            Assert.That(PageRangeEditor.Replace("one", 2, 1, "x", 100).Error, Is.Not.Null);
            Assert.That(PageRangeEditor.Replace("one", 1, 2, "x", 100).Error, Is.Not.Null);
        });
    }

    [Test]
    public void RejectsResultLargerThanPageLimitWithoutReturningContent() {
        PageRangeEditResult result = PageRangeEditor.Replace("one", 1, 1, "too long", 4);

        Assert.That(result.Content, Is.Null);
        Assert.That(result.Error, Does.Contain("size limit"));
        Assert.That(result.IsSuccess, Is.False);
    }

    // The replacement is 11 characters and the range is line 2 of 16 characters, so the real result
    // is 23 characters: a budget below that must fail and one at or above it must not.
    [TestCase(13, true)]
    [TestCase(23, false)]
    [TestCase(25, false)]
    public void TheSizeLimitIsCheckedAgainstTheRealResultingLength(int maximumChars, bool rejected) {
        PageRangeEditResult result = PageRangeEditor.Replace("one\r\ntwo\r\nthree", 2, 2, "replacement", maximumChars);

        Assert.That(result.Error is not null, Is.EqualTo(rejected));
    }

    [Test]
    public void ALoneCarriageReturnAtTheEndIsOneLineBreak() {
        PageRangeEditResult result = PageRangeEditor.Replace("one\r", 1, 1, "x", 100);

        Assert.That(result.Content, Is.EqualTo("x\r"));
    }

    [Test]
    public void TwoLineFeedsAreTwoLineBreaks() {
        PageRangeEditResult result = PageRangeEditor.Replace("a\n\nb", 3, 3, "c", 100);

        Assert.That(result.Content, Is.EqualTo("a\n\nc"));
    }

    [Test]
    public void MissingSourceIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => PageRangeEditor.Replace(null!, 1, 1, "x", 100), Throws.ArgumentNullException);
            Assert.That(() => PageRangeEditor.Replace("one", 1, 1, null!, 100), Throws.ArgumentNullException);
            Assert.That(() => PageRangeEditor.Replace("one", 1, 1, "x", 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ReplacesFinalEmptyLineWithoutChangingTheExistingTrailingNewline() {
        PageRangeEditResult result = PageRangeEditor.Replace("one\n", 2, 2, "two", 20);

        Assert.That(result.Content, Is.EqualTo("one\ntwo"));
    }

    [Test]
    public void AnExpectedFirstLineThatDoesNotMatchRefusesTheEdit() {
        PageRangeEditResult result = PageRangeEditor.Replace(
            "one\r\ntwo\r\nthree", 2, 2, "changed", 100, expectFirst: "not two", expectLast: null);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Error, Does.Contain("Line 2 is not the expected text"));
        });
    }

    [Test]
    public void AExpectedLastLineThatDoesNotMatchRefusesTheEdit() {
        PageRangeEditResult result = PageRangeEditor.Replace(
            "one\r\ntwo\r\nthree", 2, 3, "changed", 100, expectFirst: null, expectLast: "not three");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Line 3 is not the expected text"));
        });
    }

    [Test]
    public void MatchingExpectedLinesAllowTheEdit() {
        PageRangeEditResult result = PageRangeEditor.Replace(
            "one\r\ntwo\r\nthree", 2, 2, "changed", 100, expectFirst: "two", expectLast: "two");

        Assert.That(result.Content, Is.EqualTo("one\r\nchanged\r\nthree"));
    }

    [Test]
    public void SeveralEditsAreAppliedAgainstTheOriginalLineNumbers() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll(
            "one\ntwo\nthree\nfour",
            [
                new PageLineEdit(2, 2, "second\nextra"),
                new PageLineEdit(4, 4, "fourth"),
            ],
            100);

        Assert.That(result.Content, Is.EqualTo("one\nsecond\nextra\nthree\nfourth"));
    }

    [Test]
    public void SeveralEditsRefuseOverlappingRanges() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll(
            "one\ntwo\nthree",
            [
                new PageLineEdit(1, 2, "first"),
                new PageLineEdit(2, 3, "second"),
            ],
            100);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("overlap at line 2"));
        });
    }

    [Test]
    public void SeveralEditsRefuseAnOutOfRangeEdit() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll(
            "one\ntwo",
            [new PageLineEdit(2, 3, "past the end")],
            100);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Edit at line 2 is outside the page"));
        });
    }

    [Test]
    public void SeveralEditsCheckTheirExpectationsAgainstTheOriginalSource() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll(
            "one\ntwo\nthree",
            [
                new PageLineEdit(3, 3, "third", "not three", null),
                new PageLineEdit(1, 1, "first", "one", "one"),
            ],
            100);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("Line 3 is not the expected text"));
        });
    }

    [Test]
    public void SeveralEditsRefuseAnEmptyList() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll("one", [], 100);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Does.Contain("No edits"));
    }

    [Test]
    public void SeveralEditsRefuseAResultLargerThanThePageLimit() {
        PageRangeEditResult result = PageRangeEditor.ReplaceAll(
            "one\ntwo",
            [new PageLineEdit(1, 1, "first"), new PageLineEdit(2, 2, "second")],
            5);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.Contain("size limit"));
        });
    }
}
