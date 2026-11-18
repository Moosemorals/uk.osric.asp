// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class OwnerInterruptCoordinatorTests {

    [Test]
    public async Task SignalWakesTheMatchingWaiterAsync() {
        OwnerInterruptCoordinator coordinator = new();
        Task wait = coordinator.WaitAsync("run", CancellationToken.None);

        Assert.That(coordinator.Signal("run"), Is.True);
        await wait;
    }

    [Test]
    public void SignalForAnUnknownRunWakesNothing() {
        OwnerInterruptCoordinator coordinator = new();

        Assert.That(coordinator.Signal("run"), Is.False);
    }

    [Test]
    public async Task CancellationStopsWaitingAndRemovesThePendingSignalAsync() {
        OwnerInterruptCoordinator coordinator = new();
        using CancellationTokenSource cancellation = new();
        Task wait = coordinator.WaitAsync("run", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(async () => await wait);

        Assert.That(coordinator.Signal("run"), Is.False);
    }

    [Test]
    public async Task ASecondWaitForTheSameRunIsRefusedAsync() {
        OwnerInterruptCoordinator coordinator = new();
        _ = coordinator.WaitAsync("run", CancellationToken.None);

        InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.WaitAsync("run", CancellationToken.None));
        Assert.That(error?.Message, Does.Contain("already exists"));
    }
}
