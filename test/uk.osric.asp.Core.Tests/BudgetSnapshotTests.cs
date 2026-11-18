// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class BudgetSnapshotTests {

    [Test]
    public void TheRequestFigureIsThePlansEstimatedTokens() {
        ChatMessage[] stored = [new("system", "prompt"), new("user", "a question"), new("assistant", "an answer")];
        ChatRequest request = Request(stored);
        ContextPlan plan = ContextPlanner.Build(request, Budget(Bytes(request)), Exact);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null, null, Budget(Bytes(request)), 0);

        Assert.That(snapshot.ContextTokens, Is.EqualTo(plan.EstimatedTokens));
    }

    [Test]
    public void TheArgumentRowStillMeasuresTheStoredTranscript() {
        ChatMessage[] stored = [.. Enumerable.Range(0, 3).Select(index => Call($"call-{index}", LongArguments()))];
        ChatRequest request = Request(stored);
        int storedLength = ArgumentsOf(stored[0]).Length;

        ContextPlan plan = ContextPlanner.Build(request, Budget(Bytes(request) - 1), Exact);
        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null, null, Budget(Bytes(request)), 0);

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

        ContextPlan plan = ContextPlanner.Build(request, Budget(budget), Exact);
        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, stored, 0, 0, null, null, Budget(Bytes(request)), 0);

        Assert.Multiple(() => {
            Assert.That(plan.IsMasked, Is.True);
            Assert.That(plan.EstimatedTokens, Is.LessThanOrEqualTo(budget));
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

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Budget(Bytes(request)), Exact), stored, 0, 0, null, null, Budget(Bytes(request)), 0);

        Assert.That(snapshot.LongestArguments, Is.EqualTo("""{"path":"/a-much-longer-path"}""".Length));
    }

    [Test]
    public void AMessageWithoutCallsContributesNoArgumentLength() {
        ChatMessage[] stored = [new("user", "just words"), new("assistant", "just an answer")];
        ChatRequest request = Request(stored);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Budget(Bytes(request)), Exact), stored, 0, 0, null, null, Budget(Bytes(request)), 0);

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

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(ContextPlanner.Build(request, Budget(Bytes(request)), Exact), stored, 0, 0, null, null, Budget(Bytes(request)), 0);

        Assert.That(snapshot.LongestResult, Is.EqualTo(90));
    }

    [Test]
    public void TheCountersAreCarriedThroughUnchanged() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Budget(Bytes(request)), Exact);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], steps: 3, toolCalls: 2, lastCompletionTokens: 513, lastMaxTokens: 900, budget: Budget(Bytes(request)), estimateError: 0);

        Assert.Multiple(() => {
            Assert.That(snapshot.Steps, Is.EqualTo(3));
            Assert.That(snapshot.ToolCalls, Is.EqualTo(2));
            Assert.That(snapshot.LastCompletionTokens, Is.EqualTo(513));
            Assert.That(snapshot.LastMaxTokens, Is.EqualTo(900));
        });
    }

    [Test]
    public void AMissingUsageCountsAsNoOutput() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Budget(Bytes(request)), Exact);

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], 0, 0, null, null, Budget(Bytes(request)), 0);

        Assert.That(snapshot.LastCompletionTokens, Is.Zero);
    }

    [Test]
    public void AnEmptyTranscriptMeasuresTheEnvelopeItWouldSend() {
        ChatRequest request = Request([]);
        ContextPlan plan = ContextPlanner.Build(request, Budget(Bytes(request)), Exact);

        ContextBudget budget = Budget(Bytes(request));
        BudgetSnapshot snapshot = BudgetSnapshot.Measure(plan, [], 1, 4, 7, 70, budget, 12);

        Assert.Multiple(() => {
            Assert.That(snapshot.ContextTokens, Is.EqualTo(Bytes(request)));
            Assert.That(snapshot.LongestArguments, Is.Zero);
            Assert.That(snapshot.LongestResult, Is.Zero);
            Assert.That(snapshot.EstimateError, Is.EqualTo(12));
            Assert.That(snapshot.EstimateErrorMagnitude, Is.EqualTo(12));
            Assert.That(snapshot.EstimateDirection, Is.EqualTo("over"));
            Assert.That(snapshot.WindowUsed, Is.EqualTo(snapshot.ContextTokens + snapshot.MaxTokens));
            Assert.That(snapshot, Is.EqualTo(new BudgetSnapshot(
                Bytes(request), 1, 4, 0, 0, 7, budget.ContextWindow, plan.MaxTokens, 70, 12)));
        });
    }

    private static ChatRequest Request(IReadOnlyList<ChatMessage> messages) => new(
        "model",
        messages,
        [],
        0.2,
        1,
        "reasoning_content");

    private static ContextBudget Budget(long promptTokens) => ContextBudget.ForPromptShare(promptTokens);

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

    // One byte is one estimated token, so a budget derived from a byte count keeps the exact
    // arithmetic the masking rules were written around.
    private static readonly TokenEstimator Exact = new(1.0, 1.0);
}
