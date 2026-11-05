// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Agent;

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
    public void LongTextStopsAtTheLastWholeWordWithinTheLimit() {
        string title = ConversationTitle.From(string.Join(' ', Enumerable.Repeat("word", 60)));

        Assert.That(title.Length, Is.LessThanOrEqualTo(200));
        Assert.That(title, Does.EndWith("\u2026"));
        Assert.That(title[..^1].Split(' '), Does.Not.Contain(""));
    }
}
