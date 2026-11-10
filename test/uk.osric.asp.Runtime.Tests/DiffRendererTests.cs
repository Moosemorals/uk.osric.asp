// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using uk.osric.asp.Core.Diff;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class DiffRendererTests {

    private readonly DiffRenderer _renderer = new();

    [Test]
    public void ASourceLineIsEscapedRatherThanWrittenAsMarkup() {
        string html = _renderer.ToHtml(
            "<script>alert('before')</script>\n& \"quoted\"",
            "<script>alert('after')</script>\n& \"quoted\"");

        Assert.Multiple(() => {
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Contain("&lt;script&gt;alert(&#39;before&#39;)&lt;/script&gt;"));
            Assert.That(html, Does.Contain("&lt;script&gt;alert(&#39;after&#39;)&lt;/script&gt;"));
            Assert.That(html, Does.Contain("&amp; &quot;quoted&quot;"));
        });
    }

    [Test]
    public void TheTableHasAHeadRowWithScopedHeaders() {
        string html = _renderer.ToHtml("one", "two");

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<table class=\"diff\">"));
            Assert.That(html, Does.Contain("<thead>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Before</th>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">After</th>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Change</th>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Line</th>"));
        });
    }

    [Test]
    public void AddedAndRemovedLinesCarryAMarkerAndAHiddenWord() {
        string html = _renderer.ToHtml("one\ntwo", "one\nTWO");

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<tr class=\"diff-removed\">"));
            Assert.That(html, Does.Contain("<span class=\"visually-hidden\">Removed</span>-"));
            Assert.That(html, Does.Contain("<tr class=\"diff-added\">"));
            Assert.That(html, Does.Contain("<span class=\"visually-hidden\">Added</span>+"));
            Assert.That(html, Does.Contain("<span class=\"visually-hidden\">Unchanged</span> "));
        });
    }

    [Test]
    public void TheHunkHeaderSpansTheTableAndCarriesTheRanges() {
        string html = _renderer.ToHtml("one\ntwo", "one\nTWO");

        Assert.That(html, Does.Contain("<tr class=\"diff-hunk\"><td colspan=\"4\">@@ -1,2 +1,2 @@</td></tr>"));
    }

    [Test]
    public void AnUnchangedPairSaysSo() {
        Assert.That(_renderer.ToHtml("one\ntwo", "one\ntwo"),
            Is.EqualTo("<p class=\"diff-empty\">No changes.</p>"));
    }

    [Test]
    public void ATooDifferentPairSaysWhyItIsCoarse() {
        string before = string.Join('\n', Enumerable.Range(0, LineDiff.MaxEditDistance + 1).Select(index => $"a{index}"));
        string after = string.Join('\n', Enumerable.Range(0, LineDiff.MaxEditDistance + 1).Select(index => $"b{index}"));

        string html = _renderer.ToHtml(before, after);

        Assert.That(html, Does.Contain("<p class=\"diff-coarse\">"));
    }

    [Test]
    public void NoInlineStyleIsWritten() {
        string html = _renderer.ToHtml("one", "two");

        Assert.That(html, Does.Not.Contain("style="));
    }

    [Test]
    public void TheUnifiedTextIsTheSameComparison() {
        string text = _renderer.ToUnifiedDiff("one\ntwo", "one\nTWO");

        Assert.That(text, Is.EqualTo("@@ -1,2 +1,2 @@\n one\n-two\n+TWO\n"));
    }

    [Test]
    public void NullDocumentsAreRefused() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => _renderer.ToHtml(null!, "one"));
            Assert.Throws<ArgumentNullException>(() => _renderer.ToHtml("one", null!));
            Assert.Throws<ArgumentNullException>(() => _renderer.ToUnifiedDiff(null!, "one"));
            Assert.Throws<ArgumentNullException>(() => _renderer.ToUnifiedDiff("one", null!));
        });
    }
}
