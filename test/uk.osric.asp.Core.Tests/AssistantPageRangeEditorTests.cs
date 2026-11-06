// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class AssistantPageRangeEditorTests {

    [Test]
    public void ReplacesInclusiveLineRangeAndPreservesUnchangedLineEndings() {
        AssistantPageRangeEditResult result = AssistantPageRangeEditor.Replace("one\r\ntwo\r\nthree", 2, 2, "replacement", 100);

        Assert.That(result.Content, Is.EqualTo("one\r\nreplacement\r\nthree"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void RangeCountsPhysicalTemplateLinesAcrossHtmlAndTemplateDelimiters() {
        const string Source = "<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"value\" %>\r\n<% } %>";

        AssistantPageRangeEditResult result = AssistantPageRangeEditor.Replace(
            Source, 5, 5, "<%= \"changed\" %>", 200);

        Assert.That(result.Content, Is.EqualTo("<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"changed\" %>\r\n<% } %>"));
    }

    [Test]
    public void RejectsInvalidLineBounds() {
        Assert.Multiple(() => {
            Assert.That(AssistantPageRangeEditor.Replace("one", 0, 1, "x", 100).Error, Is.Not.Null);
            Assert.That(AssistantPageRangeEditor.Replace("one", 2, 1, "x", 100).Error, Is.Not.Null);
            Assert.That(AssistantPageRangeEditor.Replace("one", 1, 2, "x", 100).Error, Is.Not.Null);
        });
    }

    [Test]
    public void RejectsResultLargerThanPageLimitWithoutReturningContent() {
        AssistantPageRangeEditResult result = AssistantPageRangeEditor.Replace("one", 1, 1, "too long", 4);

        Assert.That(result.Content, Is.Null);
        Assert.That(result.Error, Does.Contain("size limit"));
    }

    [Test]
    public void ReplacesFinalEmptyLineWithoutChangingTheExistingTrailingNewline() {
        AssistantPageRangeEditResult result = AssistantPageRangeEditor.Replace("one\n", 2, 2, "two", 20);

        Assert.That(result.Content, Is.EqualTo("one\ntwo"));
    }
}
