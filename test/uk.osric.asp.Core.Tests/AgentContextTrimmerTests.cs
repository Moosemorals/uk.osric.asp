// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Agent;

[TestFixture]
public sealed class AgentContextTrimmerTests {

    [Test]
    public void KeepsTheSystemPromptAndNewestUserTurn() {
        AgentChatMessage[] messages = [
            new("system", "prompt"),
            new("user", "old"),
            new("assistant", "old answer"),
            new("user", "latest"),
        ];

        AgentContextTrimResult result = AgentContextTrimmer.Trim(messages, 20);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["prompt", "latest"]));
        Assert.That(result.WasTrimmed, Is.True);
    }

    [Test]
    public void DropsToolCallTurnsAsAUnit() {
        AgentChatMessage[] messages = [
            new("system", "s"),
            new("user", "old"),
            new("assistant", "", "[{\"id\":\"call-1\"}]"),
            new("tool", "result", ToolCallId: "call-1"),
            new("assistant", "old answer"),
            new("user", "new"),
        ];

        AgentContextTrimResult result = AgentContextTrimmer.Trim(messages, 10);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["s", "new"]));
        Assert.That(result.Messages.Any(message => message.Role == "tool"), Is.False);
    }

    [Test]
    public void ReportsWhenTheNewestTurnCannotFit() {
        AgentChatMessage[] messages = [new("system", "prompt"), new("user", "too long")];

        AgentContextTrimResult result = AgentContextTrimmer.Trim(messages, 5);

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Messages, Is.Empty);
    }

    [Test]
    public void KeepsTheNewestWholeTurnWhenAnOlderTurnMustBeDropped() {
        AgentChatMessage[] messages = [
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
            new("user", "c"), new("assistant", "d"),
        ];

        AgentContextTrimResult result = AgentContextTrimmer.Trim(messages, 3);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["s", "c", "d"]));
        Assert.That(result.DroppedMessages, Is.EqualTo(2));
    }
}
