// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the two clocks and the slot a committed stream holds.</summary>
[TestFixture]
public sealed class RenderLifetimeTests {

    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() => _metrics.Dispose();

    [Test]
    public async Task ALiveStreamMovesTheSlotFromRenderToStreamAsync() {
        PageBudget budget = Budget(renderSlots: 1, streamSlots: 1, silence: 30, ceiling: 60);
        RenderLifetime lifetime = new(budget, PagePath.Normalize("/stream"), _metrics, new CancellationTokenSource());

        try {
            Assert.That(budget.RenderSlots.CurrentCount, Is.EqualTo(0), "the render slot is held before the commit");
            Assert.That(budget.StreamSlots.CurrentCount, Is.EqualTo(1));

            await lifetime.BeginStreamAsync();

            Assert.Multiple(() => {
                Assert.That(budget.RenderSlots.CurrentCount, Is.EqualTo(1), "the commit releases the render slot");
                Assert.That(budget.StreamSlots.CurrentCount, Is.EqualTo(0), "the stream takes a stream slot");
            });

            lifetime.Dispose();

            // Dispose is also in the finally, so a failed assertion here still releases the slots.
            Assert.Multiple(() => {
                Assert.That(budget.RenderSlots.CurrentCount, Is.EqualTo(1));
                Assert.That(budget.StreamSlots.CurrentCount, Is.EqualTo(1), "ending the stream releases its slot");
            });
        } finally {
            lifetime.Dispose();
            budget.Dispose();
        }
    }

    [Test]
    public async Task AFullStreamPoolRefusesTheStreamAsync() {
        PageBudget budget = Budget(renderSlots: 2, streamSlots: 1, silence: 30, ceiling: 60, renders: 2);
        RenderLifetime holder = new(budget, PagePath.Normalize("/one"), _metrics, new CancellationTokenSource());
        RenderLifetime refused = new(budget, PagePath.Normalize("/two"), _metrics, new CancellationTokenSource());

        try {
            await holder.BeginStreamAsync();

            await Assert.ThrowsAsync<PageStreamRefusedException>(() => refused.BeginStreamAsync());
        } finally {
            holder.Dispose();
            refused.Dispose();
            budget.Dispose();
        }
    }

    [Test]
    public async Task AStreamReArmsItsSilenceClockAsync() {
        PageBudget budget = Budget(renderSlots: 1, streamSlots: 1, silence: 1, ceiling: 30);
        RenderLifetime lifetime = new(budget, PagePath.Normalize("/stream"), _metrics, new CancellationTokenSource());

        try {
            await lifetime.BeginStreamAsync();
            await Task.Delay(200);
            lifetime.ReArm();

            Assert.That(lifetime.Token.IsCancellationRequested, Is.False,
                "a flush re-arms the silence clock rather than leaving the original deadline in place");

            await Task.Delay(1_500);

            Assert.That(lifetime.Token.IsCancellationRequested, Is.True,
                "the re-armed clock still ends a stream that flushes nothing more");
        } finally {
            lifetime.Dispose();
            budget.Dispose();
        }
    }

    [Test]
    public async Task AStreamThatGoesQuietEndsAsSilenceAsync() {
        PageBudget budget = Budget(renderSlots: 1, streamSlots: 1, silence: 0.1, ceiling: 30);
        RenderLifetime lifetime = new(budget, PagePath.Normalize("/stream"), _metrics, new CancellationTokenSource());

        try {
            await lifetime.BeginStreamAsync();
            await Task.Delay(300);
            lifetime.Abandon(clientLeft: false);

            Assert.That(lifetime.Outcome, Is.EqualTo(PageMetrics.StreamSilence));
        } finally {
            // Dispose leaves an abandoned lifetime's source alone, so the test stands in for the
            // continuation that disposes it once the page has stopped.
            lifetime.Dispose();
            lifetime.DisposeDeadline();
            budget.Dispose();
        }
    }

    [Test]
    public async Task AStreamAtItsCeilingEndsAsCeilingAsync() {
        PageBudget budget = Budget(renderSlots: 1, streamSlots: 1, silence: 30, ceiling: 0.1);
        RenderLifetime lifetime = new(budget, PagePath.Normalize("/stream"), _metrics, new CancellationTokenSource());

        try {
            await lifetime.BeginStreamAsync();
            await Task.Delay(300);
            lifetime.Abandon(clientLeft: false);

            Assert.That(lifetime.Outcome, Is.EqualTo(PageMetrics.StreamCeiling));
        } finally {
            lifetime.Dispose();
            lifetime.DisposeDeadline();
            budget.Dispose();
        }
    }

    private static PageBudget Budget(int renderSlots, int streamSlots, double silence, double ceiling, int renders = 1) {
        RenderOptions render = TestOptions.Render with { Slots = renderSlots, TimeoutSeconds = 5 };
        StreamOptions stream = TestOptions.Stream with { Slots = streamSlots, SilenceSeconds = silence, MaxSeconds = ceiling };

        PageBudget budget = new(render, TestOptions.Compile, stream);

        // The renderer takes the render slot before it opens a lifetime, so a test opening one by
        // hand has to hold it too, or the release in Dispose finds the semaphore already full.
        for (int i = 0; i < renders; i++) {
            budget.RenderSlots.Wait();
        }

        return budget;
    }
}
