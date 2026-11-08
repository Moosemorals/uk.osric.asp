// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ConversationTitleTests {

    [Test]
    public void UsesOnlyTheFirstLine() {
        Assert.That(ConversationTitle.From("Make a page\nIgnore this"), Is.EqualTo("Make a page"));
    }

    [Test]
    public void CollapsesWhitespace() {
        Assert.That(ConversationTitle.From("  Make\t a   page  "), Is.EqualTo("Make a page"));
    }

    [Test]
    public void EmptyTextUsesTheNewConversationTitle() {
        Assert.That(ConversationTitle.From(" \r\n details"), Is.EqualTo("New conversation"));
    }

    [Test]
    public void MissingTextUsesTheNewConversationTitle() {
        Assert.That(ConversationTitle.From(null), Is.EqualTo(ConversationTitle.NewConversation));
    }

    [Test]
    public void ALeadingLineBreakLeavesNothingOnTheFirstLine() {
        Assert.That(ConversationTitle.From("\ndetails"), Is.EqualTo(ConversationTitle.NewConversation));
    }

    [Test]
    public void ATitleOfExactlyTheLimitIsKeptWhole() {
        string text = new('a', ConversationTitle.MaximumLength);

        Assert.That(ConversationTitle.From(text), Is.EqualTo(text));
    }

    [Test]
    public void AWordWithNoBoundaryIsCutAtTheLimit() {
        string title = ConversationTitle.From(new string('x', 205));

        Assert.That(title, Has.Length.EqualTo(ConversationTitle.MaximumLength));
        Assert.That(title, Does.EndWith("\u2026"));
    }

    [Test]
    public void LongTextStopsAtTheLastWholeWordWithinTheLimit() {
        string title = ConversationTitle.From(string.Join(' ', Enumerable.Repeat("word", 60)));

        Assert.That(title.Length, Is.LessThanOrEqualTo(200));
        Assert.That(title, Does.EndWith("\u2026"));
        Assert.That(title[..^1].Split(' '), Does.Not.Contain(""));
    }
}
