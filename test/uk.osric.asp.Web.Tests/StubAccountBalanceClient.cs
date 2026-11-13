// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>An <see cref="IAccountBalanceClient"/> that answers without a socket.</summary>
internal sealed class StubAccountBalanceClient : IAccountBalanceClient {

    /// <summary>The balance to answer with, or <see langword="null"/> for a provider that did not answer.</summary>
    public AccountBalance? Balance { get; init; }

    /// <inheritdoc/>
    public Task<AccountBalance?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Balance);
}
