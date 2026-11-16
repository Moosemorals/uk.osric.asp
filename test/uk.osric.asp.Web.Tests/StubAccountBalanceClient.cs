// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>An <see cref="IAccountBalanceClient"/> that answers without a socket.</summary>
internal sealed class StubAccountBalanceClient : IAccountBalanceClient {

    /// <summary>The answer to repeat, which defaults to a provider that did not answer.</summary>
    public AccountBalanceRead Read { get; init; } = AccountBalanceRead.Failed(AccountBalanceReason.Error);

    /// <summary>Answers to give before <see cref="Read"/>, for a sampler test that changes its mind.</summary>
    public Queue<AccountBalanceRead> Reads { get; } = new();

    /// <inheritdoc/>
    public Task<AccountBalanceRead> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Reads.Count > 0 ? Reads.Dequeue() : Read);
}
