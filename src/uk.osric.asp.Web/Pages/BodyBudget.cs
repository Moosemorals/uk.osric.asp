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
    /// <param name="maxBytes">How much body the request may buffer before it is refused.</param>
    public BodyBudget(int maxBytes) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        MaxBytes = maxBytes;
    }

    /// <summary>How much body one request may buffer.</summary>
    public int MaxBytes { get; }

    /// <summary>How much body this budget has left to give.</summary>
    /// <remarks>An embedded layer renders into a budget of this size, so its whole body fits.</remarks>
    public int Remaining => MaxBytes - Volatile.Read(ref _written);

    /// <summary>Charges one write against the budget.</summary>
    /// <param name="bytes">The UTF-8 length of the text about to be written.</param>
    /// <returns>Whether the write fits; nothing is charged when it does not.</returns>
    public bool TryCharge(int bytes) {
        if (_written + bytes > MaxBytes) {
            return false;
        }

        _written += bytes;
        return true;
    }
}
