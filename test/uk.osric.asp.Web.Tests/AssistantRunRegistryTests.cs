// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantRunRegistryTests {

    [Test]
    public async Task ReplaysEventsAfterTheRequestedSequenceAsync() {
        AssistantRunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        Assert.That(registry.TryStart("run-1", 1, out AssistantRunStream? stream), Is.True);
        stream!.Publish("run.started", new { model = "test" });
        stream.Publish("content.delta", new { text = "answer" });
        registry.Complete("run-1", 1);

        AssistantEvent[] replay = await CollectAsync(stream, 1);

        Assert.That(replay.Select(item => item.Type), Is.EqualTo(["content.delta"]));
        Assert.That(replay[0].EventId, Is.EqualTo("run-1:2"));
    }

    [Test]
    public async Task RetainedJournalSupportsMultipleSubscribersAsync() {
        AssistantRunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        registry.TryStart("run-1", 1, out AssistantRunStream? stream);
        stream!.Publish("content.delta", new { text = "one" });
        stream.Publish("content.delta", new { text = "two" });
        registry.Complete("run-1", 1);

        AssistantEvent[] first = await CollectAsync(stream, 0);
        AssistantEvent[] second = await CollectAsync(stream, 0);

        Assert.That(first.Select(item => item.EventId), Is.EqualTo(second.Select(item => item.EventId)));
    }

    [Test]
    public async Task TruncatedJournalResetsInsteadOfReturningAnIncompleteReplayAsync() {
        AssistantOptions options = new() { JournalMaxEvents = 2 };
        AssistantRunRegistry registry = new(options, TimeProvider.System);
        registry.TryStart("run-1", 1, out AssistantRunStream? stream);
        stream!.Publish("content.delta", new { text = "one" });
        stream.Publish("content.delta", new { text = "two" });
        stream.Publish("content.delta", new { text = "three" });
        registry.Complete("run-1", 1);

        AssistantEvent[] replay = await CollectAsync(stream, 0);

        Assert.That(replay.Select(item => item.Type), Is.EqualTo(["stream.reset"]));
    }

    [Test]
    public void AllowsOnlyOneActiveRunForAConversationAndStopCancelsIt() {
        AssistantRunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        Assert.That(registry.TryStart("run-1", 1, out AssistantRunStream? first), Is.True);

        Assert.That(registry.TryStart("run-2", 1, out _), Is.False);
        Assert.That(registry.ActiveRun(1), Is.EqualTo("run-1"));
        Assert.That(registry.Stop("run-1"), Is.True);
        Assert.That(first!.StopToken.IsCancellationRequested, Is.True);
    }

    [Test]
    public void QueueRejectsRunsBeyondItsConfiguredCapacity() {
        AssistantRunQueue queue = new(new AssistantOptions { MaxQueuedRuns = 1 });

        Assert.That(queue.TryEnqueue(new AssistantQueuedRun(1, "run-1", "owner")), Is.True);
        Assert.That(queue.TryEnqueue(new AssistantQueuedRun(2, "run-2", "owner")), Is.False);
    }

    [Test]
    public void ExpiredCompletedJournalIsNotReturnedOnLookup() {
        AdvancingTimeProvider clock = new();
        AssistantRunRegistry registry = new(new AssistantOptions { JournalTtlMinutes = 1 }, clock);
        Assert.That(registry.TryStart("run-1", 1, out _), Is.True);
        registry.Complete("run-1", 1);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.That(registry.Find("run-1"), Is.Null);
    }

    private static async Task<AssistantEvent[]> CollectAsync(AssistantRunStream stream, long after) {
        List<AssistantEvent> events = [];
        await foreach (AssistantEvent @event in stream.ReadAfterAsync(after, CancellationToken.None)) {
            events.Add(@event);
        }
        return [.. events];
    }

    private sealed class AdvancingTimeProvider : TimeProvider {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
