// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class BudgetSnapshotTests {

    [Test]
    public void TheContextFigureIsWhatTheTrimmerMeasures() {
        ChatMessage[] messages = [
            new("system", "prompt"),
            new("user", "a question"),
            new("assistant", "an answer"),
        ];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(messages, 0, 0, null);

        Assert.That(snapshot.ContextChars, Is.EqualTo(ContextTrimmer.MeasureChars(messages)));
    }

    [Test]
    public void TheContextFigureCountsTheToolCallJsonAsWellAsTheContent() {
        ChatMessage[] messages = [
            new("assistant", "", """[{"id":"call-1","type":"function","function":{"name":"read_page","arguments":"{}"}}]"""),
        ];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(messages, 0, 0, null);

        Assert.That(snapshot.ContextChars, Is.EqualTo(messages[0].ToolCallsJson!.Length));
    }

    [Test]
    public void TheLongestArgumentIsTheLongestSerialisedCallInTheTranscript() {
        ChatMessage[] messages = [
            new("assistant", "", Call("call-1", """{"path":"/short"}""")),
            new("assistant", "", Call("call-2", """{"path":"/a-much-longer-path"}""")),
        ];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(messages, 0, 0, null);

        Assert.That(snapshot.LongestArguments, Is.EqualTo("""{"path":"/a-much-longer-path"}""".Length));
    }

    [Test]
    public void AMessageWithoutCallsContributesNoArgumentLength() {
        ChatMessage[] messages = [new("user", "just words"), new("assistant", "just an answer")];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(messages, 0, 0, null);

        Assert.That(snapshot.LongestArguments, Is.Zero);
    }

    [Test]
    public void TheLongestResultCountsOnlyToolMessages() {
        ChatMessage[] messages = [
            new("assistant", new string('a', 400)),
            new("tool", new string('b', 90), ToolCallId: "call-1", ToolName: "read_page"),
            new("tool", new string('c', 30), ToolCallId: "call-2", ToolName: "read_page"),
        ];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(messages, 0, 0, null);

        Assert.That(snapshot.LongestResult, Is.EqualTo(90));
    }

    [Test]
    public void TheCountersAreCarriedThroughUnchanged() {
        BudgetSnapshot snapshot = BudgetSnapshot.Measure([], steps: 3, toolCalls: 2, completionTokens: 513);

        Assert.Multiple(() => {
            Assert.That(snapshot.Steps, Is.EqualTo(3));
            Assert.That(snapshot.ToolCalls, Is.EqualTo(2));
            Assert.That(snapshot.CompletionTokens, Is.EqualTo(513));
        });
    }

    [Test]
    public void AMissingUsageCountsAsNoOutput() {
        BudgetSnapshot snapshot = BudgetSnapshot.Measure([], 0, 0, null);

        Assert.That(snapshot.CompletionTokens, Is.Zero);
    }

    [Test]
    public void AnEmptyTranscriptMeasuresOnlyWhatItIsGiven() {
        BudgetSnapshot snapshot = BudgetSnapshot.Measure([], 1, 4, 7);

        Assert.Multiple(() => {
            Assert.That(snapshot.ContextChars, Is.Zero);
            Assert.That(snapshot.LongestArguments, Is.Zero);
            Assert.That(snapshot.LongestResult, Is.Zero);
            Assert.That(snapshot, Is.EqualTo(new BudgetSnapshot(0, 1, 4, 0, 0, 7)));
        });
    }

    private static string Call(string id, string arguments) => JsonSerializer.Serialize(new[] {
        new { id, type = "function", function = new { name = "read_page", arguments } },
    });
}
