// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantToolApprovalCoordinatorTests {

    [TestCase(true)]
    [TestCase(false)]
    public async Task SignalResolvesTheMatchingWaiter(bool approve) {
        AssistantApprovalResolutionResult decision = approve
            ? AssistantApprovalResolutionResult.Approved
            : AssistantApprovalResolutionResult.Denied;
        AssistantToolApprovalCoordinator coordinator = new();
        Task<AssistantApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", CancellationToken.None);

        Assert.That(coordinator.Signal("run", "call", decision), Is.True);
        Assert.That(await wait, Is.EqualTo(decision));
    }

    [Test]
    public void SignalForUnknownOrFailedDecisionDoesNotAuthorizeExecution() {
        AssistantToolApprovalCoordinator coordinator = new();

        Assert.That(coordinator.Signal("run", "call", AssistantApprovalResolutionResult.Approved), Is.False);
        Assert.That(coordinator.Signal("run", "call", AssistantApprovalResolutionResult.Conflict), Is.False);
    }

    [Test]
    public async Task CancellationStopsWaitingAndRemovesThePendingSignalAsync() {
        AssistantToolApprovalCoordinator coordinator = new();
        using CancellationTokenSource cancellation = new();
        Task<AssistantApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", cancellation.Token);
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await wait);

        Assert.That(coordinator.Signal("run", "call", AssistantApprovalResolutionResult.Approved), Is.False);
    }
}
