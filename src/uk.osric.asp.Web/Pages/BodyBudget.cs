// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The body allowance one request's whole layout chain shares.</summary>
/// <remarks>
/// A layout wraps the page, so the chrome is part of the same response: handing each layer the whole
/// allowance would let a chain buffer several times the configured maximum. The layer that runs out
/// is the one named in the error.
/// </remarks>
internal sealed class BodyBudget {

    private int _written;

    /// <summary>Creates the budget.</summary>
    /// <param name="maxChars">How much body the request may buffer before it is refused.</param>
    public BodyBudget(int maxChars) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);

        MaxChars = maxChars;
    }

    /// <summary>How much body one request may buffer.</summary>
    public int MaxChars { get; }

    /// <summary>Charges one write against the budget.</summary>
    /// <param name="chars">The length of the text about to be written.</param>
    /// <returns>Whether the write fits; nothing is charged when it does not.</returns>
    public bool TryCharge(int chars) {
        if (_written + chars > MaxChars) {
            return false;
        }

        _written += chars;
        return true;
    }
}
