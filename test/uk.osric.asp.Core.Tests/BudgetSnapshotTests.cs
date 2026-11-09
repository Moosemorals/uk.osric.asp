// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class BudgetSnapshotTests {

    [Test]
    public void TheRequestFigureIsThePlansSentBytes() {
        ChatMessage[] stored = [new("system", "prompt"), new("user", "a question"), new("assistant", "an answer")];
        ChatRequest request = Request(stored);
        ContextPlan plan = ContextPlanner.Build(request, Bytes(request));

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null);

        Assert.That(snapshot.SentBytes, Is.EqualTo(plan.SentBytes));
    }

    [Test]
    public void TheArgumentRowStillMeasuresTheStoredTranscript() {
        ChatMessage[] stored = [.. Enumerable.Range(0, 3).Select(index => Call($"call-{index}", LongArguments()))];
        ChatRequest request = Request(stored);
        int storedLength = ArgumentsOf(stored[0]).Length;

        ContextPlan plan = ContextPlanner.Build(request, Bytes(request) - 1);
        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null);

        Assert.Multiple(() => {
            Assert.That(plan.IsMasked, Is.True, "the fixture only proves masking happened if it did");
            Assert.That(snapshot.LongestArguments, Is.EqualTo(storedLength));
        });
    }

    [Test]
    public void TheResultRowStillMeasuresTheStoredTranscript() {
        ChatMessage[] stored = [.. Enumerable.Range(0, 10).Select(index => Result(4_000, $"call-{index}"))];
        ChatRequest request = Request(stored);
        long budget = Bytes(request) - 2_000;

        ContextPlan plan = ContextPlanner.Build(request, budget);
        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null);

        Assert.Multiple(() => {
            Assert.That(plan.IsMasked, Is.True);
            Assert.That(plan.SentBytes, Is.LessThanOrEqualTo(budget));
            Assert.That(snapshot.LongestResult, Is.EqualTo(4_000));
        });
    }

    [Test]
    public void TheLongestArgumentIsTheLongestSerialisedCallInTheTranscript() {
        ChatMessage[] stored = [
            Call("call-1", """{"path":"/short"}"""),
            Call("call-2", """{"path":"/a-much-longer-path"}"""),
        ];
        ChatRequest request = Request(stored);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Bytes(request)), stored, 0, 0, null);

        Assert.That(snapshot.LongestArguments, Is.EqualTo("""{"path":"/a-much-longer-path"}""".Length));
    }

    [Test]
    public void AMessageWithoutCallsContributesNoArgumentLength() {
        ChatMessage[] stored = [new("user", "just words"), new("assistant", "just an answer")];
        ChatRequest request = Request(stored);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Bytes(request)), stored, 0, 0, null);

        Assert.That(snapshot.LongestArguments, Is.Zero);
    }

    [Test]
    public void TheLongestResultCountsOnlyToolMessages() {
        ChatMessage[] stored = [
            new("assistant", new string('a', 400)),
            Result(90, "call-1"),
            Result(30, "call-2"),
        ];
        ChatRequest request = Request(stored);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Bytes(request)), stored, 0, 0, null);

        Assert.That(snapshot.LongestResult, Is.EqualTo(90));
    }

    [Test]
    public void TheCountersAreCarriedThroughUnchanged() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Bytes(request));

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], steps: 3, toolCalls: 2, completionTokens: 513);

        Assert.Multiple(() => {
            Assert.That(snapshot.Steps, Is.EqualTo(3));
            Assert.That(snapshot.ToolCalls, Is.EqualTo(2));
            Assert.That(snapshot.CompletionTokens, Is.EqualTo(513));
        });
    }

    [Test]
    public void AMissingUsageCountsAsNoOutput() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Bytes(request));

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], 0, 0, null);

        Assert.That(snapshot.CompletionTokens, Is.Zero);
    }

    [Test]
    public void AnEmptyTranscriptMeasuresTheEnvelopeItWouldSend() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Bytes(request));

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], 1, 4, 7);

        Assert.Multiple(() => {
            Assert.That(snapshot.SentBytes, Is.EqualTo(Bytes(request)));
            Assert.That(snapshot.LongestArguments, Is.Zero);
            Assert.That(snapshot.LongestResult, Is.Zero);
            Assert.That(snapshot, Is.EqualTo(new BudgetSnapshot(Bytes(request), 1, 4, 0, 0, 7)));
        });
    }

    private static ChatRequest Request(IReadOnlyList<ChatMessage> messages) => new(
        "model",
        messages,
        [],
        0.2,
        20,
        "reasoning_content",
        IncludeUsage: true);

    private static ChatMessage Result(int length, string id) =>
        new("tool", new string('x', length), ToolCallId: id, ToolName: "read_page");

    private static ChatMessage Call(string id, string arguments) => new(
        "assistant",
        "",
        JsonSerializer.Serialize(new[] {
            new { id, type = "function", function = new { name = "write_page", arguments } },
        }));

    private static string LongArguments() => $$"""{"path":"/blog","content":"{{new string('x', 1_200)}}"}""";

    private static string ArgumentsOf(ChatMessage message) {
        using JsonDocument calls = JsonDocument.Parse(message.ToolCallsJson!);
        return calls.RootElement[0].GetProperty("function").GetProperty("arguments").GetString()!;
    }

    private static long Bytes(ChatRequest request) => ChatRequestJson.MeasureBytes(request);
}
