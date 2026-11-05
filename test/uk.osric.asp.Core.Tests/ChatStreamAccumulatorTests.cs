// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Agent;

[TestFixture]
public sealed class ChatStreamAccumulatorTests {

    [Test]
    public void AccumulatesContentReasoningFinishAndUsage() {
        AgentChatStreamAccumulator accumulator = new();
        accumulator.Add(new AgentContentDelta("Hello "));
        accumulator.Add(new AgentReasoningDelta("Think"));
        accumulator.Add(new AgentContentDelta("world"));
        accumulator.Add(new AgentUsageEvent(new AgentUsage(12, 4)));
        accumulator.Add(new AgentFinished("stop"));

        AgentChatResponse response = accumulator.Complete();

        Assert.Multiple(() => {
            Assert.That(response.Content, Is.EqualTo("Hello world"));
            Assert.That(response.Reasoning, Is.EqualTo("Think"));
            Assert.That(response.FinishReason, Is.EqualTo("stop"));
            Assert.That(response.Usage, Is.EqualTo(new AgentUsage(12, 4)));
        });
    }

    [Test]
    public void JoinsToolCallFragmentsByIndexAndOrdersCallsByProviderIndex() {
        AgentChatStreamAccumulator accumulator = new();
        accumulator.Add(new AgentToolCallDelta(1, "call-b", "write_page", "{\"path\":"));
        accumulator.Add(new AgentToolCallDelta(0, "call-a", "read_page", "{\"path\":"));
        accumulator.Add(new AgentToolCallDelta(1, null, null, "\"/b\"}"));
        accumulator.Add(new AgentToolCallDelta(0, null, null, "\"/a\"}"));

        IReadOnlyList<AgentToolCall> calls = accumulator.Complete().ToolCalls;

        Assert.That(calls, Is.EqualTo([
            new AgentToolCall(0, "call-a", "read_page", "{\"path\":\"/a\"}"),
            new AgentToolCall(1, "call-b", "write_page", "{\"path\":\"/b\"}"),
        ]));
    }

    [Test]
    public void MissingToolCallIdOrNameRemainsVisibleForSafeToolErrorHandling() {
        AgentChatStreamAccumulator accumulator = new();
        accumulator.Add(new AgentToolCallDelta(0, null, null, "{}"));

        AgentToolCall call = accumulator.Complete().ToolCalls.Single();

        Assert.That(call.Id, Is.Null);
        Assert.That(call.Name, Is.Null);
        Assert.That(call.Arguments, Is.EqualTo("{}"));
    }

    [Test]
    public void EmptyProviderResponseHasNoDelta() {
        Assert.That(new AgentChatStreamAccumulator().Complete().HasContent, Is.False);
    }
}
