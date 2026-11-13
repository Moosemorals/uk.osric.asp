// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>Answers the conversation page's balance question without a socket.</summary>
internal sealed class StubAccountBalanceClient : IAccountBalanceClient {

    /// <summary>The balance a fixture shows unless it asks for another one, including none.</summary>
    public static AccountBalance Default { get; } =
        new(true, [new CurrencyBalance("CNY", "110.00", "10.00", "100.00")]);

    /// <summary>The balance to answer with; <see langword="null"/> stands for a provider that did not answer.</summary>
    public AccountBalance? Balance { get; init; } = Default;

    /// <inheritdoc/>
    public Task<AccountBalance?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Balance);
}
