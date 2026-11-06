// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ContextTrimmerTests {

    [Test]
    public void KeepsTheSystemPromptAndNewestUserTurn() {
        ChatMessage[] messages = [
            new("system", "prompt"),
            new("user", "old"),
            new("assistant", "old answer"),
            new("user", "latest"),
        ];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 20);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["prompt", "latest"]));
        Assert.That(result.IsTrimmed, Is.True);
    }

    [Test]
    public void DropsToolCallTurnsAsAUnit() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "old"),
            new("assistant", "", "[{\"id\":\"call-1\"}]"),
            new("tool", "result", ToolCallId: "call-1"),
            new("assistant", "old answer"),
            new("user", "new"),
        ];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 10);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["s", "new"]));
        Assert.That(result.Messages.Any(message => message.Role == "tool"), Is.False);
    }

    [Test]
    public void ReportsWhenTheNewestTurnCannotFit() {
        ChatMessage[] messages = [new("system", "prompt"), new("user", "too long")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 5);

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Messages, Is.Empty);
    }

    [Test]
    public void KeepsTheNewestWholeTurnWhenAnOlderTurnMustBeDropped() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
            new("user", "c"), new("assistant", "d"),
        ];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 3);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["s", "c", "d"]));
        Assert.That(result.DroppedMessages, Is.EqualTo(2));
    }
}
