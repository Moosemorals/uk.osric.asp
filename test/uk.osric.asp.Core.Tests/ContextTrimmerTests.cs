// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

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

    [Test]
    public void ATightBudgetIsNotANoteUntilSomethingWasDropped() {
        ChatMessage[] messages = [new("system", "s"), new("user", "a")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 100);

        Assert.That(result.DroppedMessages, Is.Zero);
        Assert.That(result.IsTrimmed, Is.False);
        Assert.That(result.TrimmingNote, Is.Null);
    }

    [Test]
    public void ADroppedTurnIsExplainedInTheNote() {
        ChatMessage[] messages = [new("system", "s"), new("user", "a"), new("assistant", "b"), new("user", "c")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 2);

        Assert.That(result.IsTrimmed, Is.True);
        Assert.That(result.TrimmingNote, Is.Not.Null.And.Contains("omitted"));
    }

    [Test]
    public void KeepsEveryTurnWhenTheBudgetAllows() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
        ];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 100);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["s", "a", "b"]));
    }

    [Test]
    public void ATranscriptWithoutASystemPromptKeepsTheNewestTurn() {
        ChatMessage[] messages = [new("assistant", "a"), new("user", "b")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 1);

        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["b"]));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void ReportsWhenTheSystemPromptAloneIsTooLong() {
        ChatMessage[] messages = [new("system", "prompt")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 5);

        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public void KeepsASystemPromptThatExactlyFillsTheBudget() {
        ChatMessage[] messages = [new("system", "prompt")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 6);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Messages.Select(message => message.Content), Is.EqualTo(["prompt"]));
    }

    [Test]
    public void ReportsWhenTheSystemFitsButTheNewestTurnDoesNot() {
        ChatMessage[] messages = [new("system", "s"), new("user", "too long")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 5);

        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public void ToolCallJsonCountsTowardsTheBudget() {
        ChatMessage[] messages = [new("system", "s"), new("user", "a"), new("assistant", "", "0123456789")];

        ContextTrimResult result = ContextTrimmer.Trim(messages, 11);

        Assert.That(result.Error, Is.Not.Null, "one character over once the tool-call JSON is counted");
    }

    [Test]
    public void MeasureCountsContentAndToolCallJson() {
        ChatMessage[] messages = [
            new("system", "prompt"),
            new("assistant", "", "0123456789"),
            new("tool", "result", ToolCallId: "call-1"),
        ];

        Assert.That(ContextTrimmer.MeasureChars(messages), Is.EqualTo(6 + 10 + 6));
    }

    [Test]
    public void MeasureOfNothingIsNothing() {
        Assert.That(ContextTrimmer.MeasureChars([]), Is.Zero);
    }

    [Test]
    public void MeasureAgreesWithTheBudgetThatRefusesATranscript() {
        ChatMessage[] messages = [new("system", "s"), new("user", "0123456789")];

        Assert.That(ContextTrimmer.MeasureChars(messages), Is.EqualTo(11));
        Assert.That(ContextTrimmer.Trim(messages, 10).Error, Is.Not.Null);
        Assert.That(ContextTrimmer.Trim(messages, 11).Error, Is.Null);
    }

    [Test]
    public void ANullTranscriptIsRefused() {
        Assert.That(() => ContextTrimmer.Trim(null!, 10), Throws.ArgumentNullException);
    }

    [Test]
    public void ABudgetOfNothingIsRefused() {
        Assert.That(() => ContextTrimmer.Trim([new("user", "a")], 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
