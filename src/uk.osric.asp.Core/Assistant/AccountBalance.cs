// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>A provider account's balance, as the provider's balance endpoint reports it.</summary>
/// <param name="IsAvailable">Whether the provider says the balance can fund API calls.</param>
/// <param name="Currencies">The balance in each currency the provider reports, in its own order.</param>
public sealed record AccountBalance(bool IsAvailable, IReadOnlyList<CurrencyBalance> Currencies);

/// <summary>One currency's balance, with the provider's own figures.</summary>
/// <param name="Currency">The currency code the provider reports, such as <c>CNY</c> or <c>USD</c>.</param>
/// <param name="Total">The total available balance, including granted and topped-up credit.</param>
/// <param name="Granted">The granted balance that has not expired.</param>
/// <param name="ToppedUp">The balance the owner has paid in.</param>
public sealed record CurrencyBalance(string Currency, string Total, string Granted, string ToppedUp);
