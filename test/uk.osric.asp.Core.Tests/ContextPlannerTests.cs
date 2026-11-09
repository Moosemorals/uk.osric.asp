// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ContextPlannerTests {

    [Test]
    public void ARequestUnderBudgetIsSentUnchanged() {
        ChatRequest request = Request([
            new("system", "prompt"),
            new("user", "a question"),
            new("assistant", "an answer"),
        ]);

        ContextPlan plan = ContextPlanner.Build(request, Bytes(request));

        Assert.Multiple(() => {
            Assert.That(plan.Request, Is.SameAs(request), "the common request keeps its prefix byte-identical");
            Assert.That(plan.OriginalBytes, Is.EqualTo(Bytes(request)));
            Assert.That(plan.SentBytes, Is.EqualTo(Bytes(request)));
            Assert.That(plan.IsMasked, Is.False);
            Assert.That(plan.IsTrimmed, Is.False);
            Assert.That(plan.Error, Is.Null);
        });
    }

    [Test]
    public void MaskingIsUsedBeforeAnyTurnIsDropped() {
        List<ChatMessage> messages = [new("user", "one")];
        for (int index = 0; index < 9; index++) {
            messages.Add(Call($"call-{index}", """{"path":"/one"}"""));
            messages.Add(Result(500, $"call-{index}"));
        }
        messages.Add(new("assistant", "old answer"));
        messages.Add(new("user", "two"));
        messages.Add(new("assistant", "done"));

        ChatRequest request = Request(messages);
        List<ChatMessage> expected = [.. messages];
        int firstResult = expected.FindIndex(message => message.Role == "tool");
        expected[firstResult] = ObservationMasker.MaskMessage(expected[firstResult]).Message;

        ContextPlan plan = ContextPlanner.Build(request, Bytes(Request(expected)));

        Assert.Multiple(() => {
            Assert.That(plan.IsMasked, Is.True);
            Assert.That(plan.MaskedToolResults, Is.EqualTo(1));
            Assert.That(plan.DroppedMessages, Is.Zero, "old payloads are cheaper than old turns");
            Assert.That(plan.Request.Messages, Has.Count.EqualTo(messages.Count));
            Assert.That(plan.Request.Messages[firstResult].Content, Does.StartWith("[tool result elided:"));
        });
    }

    [Test]
    public void TheOldestEligibleMessageIsMaskedFirst() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 10).Select(index => Result(1_000, $"call-{index}"))];
        ChatRequest request = Request(messages);
        List<ChatMessage> expected = [.. messages];
        expected[0] = ObservationMasker.MaskMessage(messages[0]).Message;

        ContextPlan plan = ContextPlanner.Build(request, Bytes(Request(expected)));

        Assert.Multiple(() => {
            Assert.That(plan.MaskedToolResults, Is.EqualTo(1));
            Assert.That(plan.Request.Messages[0].Content, Does.StartWith("[tool result elided:"));
            Assert.That(plan.Request.Messages[1].Content, Is.EqualTo(messages[1].Content),
                "the request fitted once the oldest payload went");
        });
    }

    [Test]
    public void DroppingStartsAtTheOldestWholeTurn() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
            new("user", "c"), new("assistant", "d"),
        ];

        ContextPlan plan = ContextPlanner.Build(Request(messages), Bytes(Request([
            new("system", "s"),
            new("user", "c"), new("assistant", "d"),
        ])));

        Assert.Multiple(() => {
            Assert.That(plan.IsTrimmed, Is.True);
            Assert.That(plan.DroppedMessages, Is.EqualTo(2));
            Assert.That(plan.Request.Messages.Select(message => message.Content), Is.EqualTo(["s", "c", "d"]));
        });
    }

    [Test]
    public void TheNewestTurnIsNeverDropped() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "old"), new("assistant", "old answer"),
            new("user", "newest"),
        ];

        ContextPlan plan = ContextPlanner.Build(Request(messages), Bytes(Request([
            new("system", "s"),
            new("user", "newest"),
        ])));

        Assert.That(plan.Request.Messages[^1].Content, Is.EqualTo("newest"));
    }

    [Test]
    public void ADroppedTurnTakesItsToolMessagesWithIt() {
        ChatMessage[] messages = [
            new("system", "s"),
            new("user", "old"),
            Call("call-1", """{"path":"/one"}"""),
            Result(20, "call-1"),
            new("assistant", "old answer"),
            new("user", "new"),
        ];

        ContextPlan plan = ContextPlanner.Build(Request(messages), Bytes(Request([
            new("system", "s"),
            new("user", "new"),
        ])));

        Assert.Multiple(() => {
            Assert.That(plan.Request.Messages.Any(message => message.Role == "tool"), Is.False);
            Assert.That(plan.Request.Messages.Select(message => message.Content), Is.EqualTo(["s", "new"]));
        });
    }

    [Test]
    public void TheSmallestRequestTooLargeIsAnErrorWithTheBytes() {
        ChatRequest request = Request([new("system", "s"), new("user", "a")]);

        ContextPlan plan = ContextPlanner.Build(request, 1);

        Assert.Multiple(() => {
            Assert.That(plan.Error, Does.Contain("needs").And.Contains("budget is 1"));
            Assert.That(plan.SentBytes, Is.EqualTo(Bytes(plan.Request)));
            Assert.That(plan.SentBytes, Is.GreaterThan(1));
        });
    }

    [Test]
    public void ThePlanReportsTheBytesOfTheRequestItReturns() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 10).Select(index => Result(1_000, $"call-{index}"))];
        ChatRequest request = Request(messages);

        ContextPlan plan = ContextPlanner.Build(request, Bytes(request) - 500);

        Assert.Multiple(() => {
            Assert.That(plan.OriginalBytes, Is.EqualTo(Bytes(request)));
            Assert.That(plan.SentBytes, Is.EqualTo(Bytes(plan.Request)));
            Assert.That(plan.SentBytes, Is.LessThan(plan.OriginalBytes));
        });
    }

    [Test]
    public void ThePlanIsStableForTheSameInput() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 10).Select(index => Result(1_000, $"call-{index}"))];
        ChatRequest request = Request(messages);
        long budget = Bytes(request) - 500;

        ContextPlan first = ContextPlanner.Build(request, budget);
        ContextPlan second = ContextPlanner.Build(request, budget);

        Assert.Multiple(() => {
            Assert.That(second.MaskedToolResults, Is.EqualTo(first.MaskedToolResults));
            Assert.That(second.MaskedArguments, Is.EqualTo(first.MaskedArguments));
            Assert.That(second.DroppedMessages, Is.EqualTo(first.DroppedMessages));
            Assert.That(second.SentBytes, Is.EqualTo(first.SentBytes));
            Assert.That(ChatRequestJson.Serialize(second.Request), Is.EqualTo(ChatRequestJson.Serialize(first.Request)));
        });
    }

    [Test]
    public void ASystemPromptAndToolsTooLargeIsAnError() {
        ChatRequest request = Request(
            [new("system", new string('s', 5_000)), new("user", "a")],
            [Tool]);

        ContextPlan plan = ContextPlanner.Build(request, Bytes(Request([], [Tool])));

        Assert.That(plan.Error, Is.Not.Null);
    }

    [Test]
    public void ANullRequestIsRefused() {
        Assert.That(() => ContextPlanner.Build(null!, 10), Throws.ArgumentNullException);
    }

    [Test]
    public void ANonPositiveBudgetIsRefused() {
        Assert.That(() => ContextPlanner.Build(Request([new("user", "a")]), 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static ToolDefinition Tool { get; } = new(
        "read_page",
        "Reads a page.",
        false,
        """{"type":"object","properties":{"path":{"type":"string"}}}""");

    private static ChatRequest Request(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools = null) => new(
        "model",
        messages,
        tools ?? [],
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

    private static long Bytes(ChatRequest request) => ChatRequestJson.MeasureBytes(request);
}
