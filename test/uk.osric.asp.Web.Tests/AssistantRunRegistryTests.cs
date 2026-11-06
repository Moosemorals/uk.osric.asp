// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantRunRegistryTests {

    [Test]
    public async Task ReplaysEventsAfterTheRequestedSequenceAsync() {
        RunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        Assert.That(registry.TryStart("run-1", 1, out RunStream? stream), Is.True);
        stream!.Publish("run.started", new { model = "test" });
        stream.Publish("content.delta", new { text = "answer" });
        registry.Complete("run-1", 1);

        RunEvent[] replay = await CollectAsync(stream, 1);

        Assert.That(replay.Select(item => item.Type), Is.EqualTo(["content.delta"]));
        Assert.That(replay[0].EventId, Is.EqualTo("run-1:2"));
    }

    [Test]
    public async Task RetainedJournalSupportsMultipleSubscribersAsync() {
        RunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        registry.TryStart("run-1", 1, out RunStream? stream);
        stream!.Publish("content.delta", new { text = "one" });
        stream.Publish("content.delta", new { text = "two" });
        registry.Complete("run-1", 1);

        RunEvent[] first = await CollectAsync(stream, 0);
        RunEvent[] second = await CollectAsync(stream, 0);

        Assert.That(first.Select(item => item.EventId), Is.EqualTo(second.Select(item => item.EventId)));
    }

    [Test]
    public async Task TruncatedJournalResetsInsteadOfReturningAnIncompleteReplayAsync() {
        AssistantOptions options = new() { JournalMaxEvents = 2 };
        RunRegistry registry = new(options, TimeProvider.System);
        registry.TryStart("run-1", 1, out RunStream? stream);
        stream!.Publish("content.delta", new { text = "one" });
        stream.Publish("content.delta", new { text = "two" });
        stream.Publish("content.delta", new { text = "three" });
        registry.Complete("run-1", 1);

        RunEvent[] replay = await CollectAsync(stream, 0);

        Assert.That(replay.Select(item => item.Type), Is.EqualTo(["stream.reset"]));
    }

    [Test]
    public void AllowsOnlyOneActiveRunForAConversationAndStopCancelsIt() {
        RunRegistry registry = new(new AssistantOptions(), TimeProvider.System);
        Assert.That(registry.TryStart("run-1", 1, out RunStream? first), Is.True);

        Assert.That(registry.TryStart("run-2", 1, out _), Is.False);
        Assert.That(registry.ActiveRun(1), Is.EqualTo("run-1"));
        Assert.That(registry.Stop("run-1"), Is.True);
        Assert.That(first!.StopToken.IsCancellationRequested, Is.True);
    }

    [Test]
    public void QueueRejectsRunsBeyondItsConfiguredCapacity() {
        RunQueue queue = new(new AssistantOptions { MaxQueuedRuns = 1 });

        Assert.That(queue.TryEnqueue(new QueuedRun(1, "run-1", "owner")), Is.True);
        Assert.That(queue.TryEnqueue(new QueuedRun(2, "run-2", "owner")), Is.False);
    }

    [Test]
    public void ExpiredCompletedJournalIsNotReturnedOnLookup() {
        AdvancingTimeProvider clock = new();
        RunRegistry registry = new(new AssistantOptions { JournalTtlMinutes = 1 }, clock);
        Assert.That(registry.TryStart("run-1", 1, out _), Is.True);
        registry.Complete("run-1", 1);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.That(registry.Find("run-1"), Is.Null);
    }

    private static async Task<RunEvent[]> CollectAsync(RunStream stream, long after) {
        List<RunEvent> events = [];
        await foreach (RunEvent @event in stream.ReadAfterAsync(after, CancellationToken.None)) {
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
