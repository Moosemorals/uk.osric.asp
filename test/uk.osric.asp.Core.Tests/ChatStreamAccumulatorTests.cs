// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ChatStreamAccumulatorTests {

    [Test]
    public void AccumulatesContentReasoningFinishAndUsage() {
        AssistantChatStreamAccumulator accumulator = new();
        accumulator.Add(new AssistantContentDelta("Hello "));
        accumulator.Add(new AssistantReasoningDelta("Think"));
        accumulator.Add(new AssistantContentDelta("world"));
        accumulator.Add(new AssistantUsageEvent(new AssistantUsage(12, 4)));
        accumulator.Add(new AssistantFinished("stop"));

        AssistantChatResponse response = accumulator.Complete();

        Assert.Multiple(() => {
            Assert.That(response.Content, Is.EqualTo("Hello world"));
            Assert.That(response.Reasoning, Is.EqualTo("Think"));
            Assert.That(response.FinishReason, Is.EqualTo("stop"));
            Assert.That(response.Usage, Is.EqualTo(new AssistantUsage(12, 4)));
        });
    }

    [Test]
    public void JoinsToolCallFragmentsByIndexAndOrdersCallsByProviderIndex() {
        AssistantChatStreamAccumulator accumulator = new();
        accumulator.Add(new AssistantToolCallDelta(1, "call-b", "write_page", "{\"path\":"));
        accumulator.Add(new AssistantToolCallDelta(0, "call-a", "read_page", "{\"path\":"));
        accumulator.Add(new AssistantToolCallDelta(1, null, null, "\"/b\"}"));
        accumulator.Add(new AssistantToolCallDelta(0, null, null, "\"/a\"}"));

        IReadOnlyList<AssistantToolCall> calls = accumulator.Complete().ToolCalls;

        Assert.That(calls, Is.EqualTo([
            new AssistantToolCall(0, "call-a", "read_page", "{\"path\":\"/a\"}"),
            new AssistantToolCall(1, "call-b", "write_page", "{\"path\":\"/b\"}"),
        ]));
    }

    [Test]
    public void MissingToolCallIdOrNameRemainsVisibleForSafeToolErrorHandling() {
        AssistantChatStreamAccumulator accumulator = new();
        accumulator.Add(new AssistantToolCallDelta(0, null, null, "{}"));

        AssistantToolCall call = accumulator.Complete().ToolCalls.Single();

        Assert.That(call.Id, Is.Null);
        Assert.That(call.Name, Is.Null);
        Assert.That(call.Arguments, Is.EqualTo("{}"));
    }

    [Test]
    public void EmptyProviderResponseHasNoDelta() {
        Assert.That(new AssistantChatStreamAccumulator().Complete().HasContent, Is.False);
    }
}
