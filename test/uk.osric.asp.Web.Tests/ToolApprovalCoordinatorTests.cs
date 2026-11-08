// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class ToolApprovalCoordinatorTests {

    [TestCase(true)]
    [TestCase(false)]
    public async Task SignalResolvesTheMatchingWaiter(bool approve) {
        ApprovalResolutionResult decision = approve
            ? ApprovalResolutionResult.Approved
            : ApprovalResolutionResult.Denied;
        ToolApprovalCoordinator coordinator = new();
        Task<ApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", CancellationToken.None);

        Assert.That(coordinator.Signal("run", "call", decision), Is.True);
        Assert.That(await wait, Is.EqualTo(decision));
    }

    [Test]
    public void SignalForUnknownOrFailedDecisionDoesNotAuthorizeExecution() {
        ToolApprovalCoordinator coordinator = new();

        Assert.That(coordinator.Signal("run", "call", ApprovalResolutionResult.Approved), Is.False);
        Assert.That(coordinator.Signal("run", "call", ApprovalResolutionResult.Conflict), Is.False);
    }

    [Test]
    public async Task CancellationStopsWaitingAndRemovesThePendingSignalAsync() {
        ToolApprovalCoordinator coordinator = new();
        using CancellationTokenSource cancellation = new();
        Task<ApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", cancellation.Token);
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await wait);

        Assert.That(coordinator.Signal("run", "call", ApprovalResolutionResult.Approved), Is.False);
    }
}
