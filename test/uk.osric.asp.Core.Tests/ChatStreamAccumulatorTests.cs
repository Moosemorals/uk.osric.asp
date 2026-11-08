// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ChatStreamAccumulatorTests {

    [Test]
    public void AccumulatesContentReasoningFinishAndUsage() {
        ChatStreamAccumulator accumulator = new();
        accumulator.Add(new ContentDelta("Hello "));
        accumulator.Add(new ReasoningDelta("Think"));
        accumulator.Add(new ContentDelta("world"));
        accumulator.Add(new UsageEvent(new TokenUsage(12, 4)));
        accumulator.Add(new Finished("stop"));

        ChatResponse response = accumulator.Complete();

        Assert.Multiple(() => {
            Assert.That(response.Content, Is.EqualTo("Hello world"));
            Assert.That(response.Reasoning, Is.EqualTo("Think"));
            Assert.That(response.FinishReason, Is.EqualTo("stop"));
            Assert.That(response.Usage, Is.EqualTo(new TokenUsage(12, 4)));
        });
    }

    [Test]
    public void JoinsToolCallFragmentsByIndexAndOrdersCallsByProviderIndex() {
        ChatStreamAccumulator accumulator = new();
        accumulator.Add(new ToolCallDelta(1, "call-b", "write_page", "{\"path\":"));
        accumulator.Add(new ToolCallDelta(0, "call-a", "read_page", "{\"path\":"));
        accumulator.Add(new ToolCallDelta(1, null, null, "\"/b\"}"));
        accumulator.Add(new ToolCallDelta(0, null, null, "\"/a\"}"));

        IReadOnlyList<ToolCall> calls = accumulator.Complete().ToolCalls;

        Assert.That(calls, Is.EqualTo([
            new ToolCall(0, "call-a", "read_page", "{\"path\":\"/a\"}"),
            new ToolCall(1, "call-b", "write_page", "{\"path\":\"/b\"}"),
        ]));
    }

    [Test]
    public void MissingToolCallIdOrNameRemainsVisibleForSafeToolErrorHandling() {
        ChatStreamAccumulator accumulator = new();
        accumulator.Add(new ToolCallDelta(0, null, null, "{}"));

        ToolCall call = accumulator.Complete().ToolCalls.Single();

        Assert.That(call.Id, Is.Null);
        Assert.That(call.Name, Is.Null);
        Assert.That(call.Arguments, Is.EqualTo("{}"));
    }

    [Test]
    public void EmptyProviderResponseHasNoDelta() {
        Assert.That(new ChatStreamAccumulator().Complete().HasContent, Is.False);
    }
}
