// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Agent;

[TestFixture]
public sealed class AgentToolApprovalCoordinatorTests {

    [TestCase(true)]
    [TestCase(false)]
    public async Task SignalResolvesTheMatchingWaiter(bool approve) {
        AgentApprovalResolutionResult decision = approve
            ? AgentApprovalResolutionResult.Approved
            : AgentApprovalResolutionResult.Denied;
        AgentToolApprovalCoordinator coordinator = new();
        Task<AgentApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", CancellationToken.None);

        Assert.That(coordinator.Signal("run", "call", decision), Is.True);
        Assert.That(await wait, Is.EqualTo(decision));
    }

    [Test]
    public void SignalForUnknownOrFailedDecisionDoesNotAuthorizeExecution() {
        AgentToolApprovalCoordinator coordinator = new();

        Assert.That(coordinator.Signal("run", "call", AgentApprovalResolutionResult.Approved), Is.False);
        Assert.That(coordinator.Signal("run", "call", AgentApprovalResolutionResult.Conflict), Is.False);
    }

    [Test]
    public async Task CancellationStopsWaitingAndRemovesThePendingSignalAsync() {
        AgentToolApprovalCoordinator coordinator = new();
        using CancellationTokenSource cancellation = new();
        Task<AgentApprovalResolutionResult> wait = coordinator.WaitAsync("run", "call", cancellation.Token);
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await wait);

        Assert.That(coordinator.Signal("run", "call", AgentApprovalResolutionResult.Approved), Is.False);
    }
}
