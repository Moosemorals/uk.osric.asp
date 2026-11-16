// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;

/// <summary>Reads the provider's money strings as numbers.</summary>
/// <remarks>
/// The figures arrive as text the provider already formatted, and a gauge takes a number. The parse is
/// invariant-culture and decimal, so a figure is read the same way wherever the site runs and no
/// rounding happens between the provider and the meter.
/// </remarks>
public static class AccountBalanceAmounts {

    /// <summary>Reads one currency's total from the text the provider sent.</summary>
    /// <param name="total">The provider's figure, such as <c>110.00</c>.</param>
    /// <param name="amount">The figure, when the text is one.</param>
    /// <returns>Whether the text was a figure.</returns>
    public static bool TryRead(string total, out decimal amount) =>
        decimal.TryParse(total, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
