// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Reads the provider account's current balance for the conversation page and the sampler.</summary>
internal interface IAccountBalanceClient {

    /// <summary>Reads the provider's balance.</summary>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The balance with its reason, which is never an exception and never a log entry.</returns>
    Task<AccountBalanceRead> GetAsync(CancellationToken cancellationToken);
}
