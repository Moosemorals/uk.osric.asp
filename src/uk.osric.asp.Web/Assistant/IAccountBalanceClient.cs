// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Reads the provider account's current balance for the conversation page.</summary>
internal interface IAccountBalanceClient {

    /// <summary>Reads the provider's balance.</summary>
    /// <param name="cancellationToken">Cancelled when the page request ends.</param>
    /// <returns>The balance, or <see langword="null"/> when the provider did not answer with one.</returns>
    Task<AccountBalance?> GetAsync(CancellationToken cancellationToken);
}
