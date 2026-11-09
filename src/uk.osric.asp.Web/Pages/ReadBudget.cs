// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The template allowance one request's whole layout chain shares.</summary>
/// <remarks>
/// The cap names the render rather than the layer, so a layout must not double the allowance simply
/// because there are two layers reading under it. A read that would go over is refused rather than
/// clamped, because half a template is a misleading answer.
/// </remarks>
internal sealed class ReadBudget {

    private int _used;

    /// <summary>Creates the counter for one render.</summary>
    /// <param name="maxChars">How much template one render may pull in.</param>
    public ReadBudget(int maxChars) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);

        MaxChars = maxChars;
    }

    /// <summary>How much template one render may pull in.</summary>
    public int MaxChars { get; }

    /// <summary>Charges one template against the budget.</summary>
    /// <param name="chars">The template's length.</param>
    /// <returns>Whether the read fits; nothing is charged when it does not.</returns>
    public bool TryCharge(int chars) {
        if (chars < 0) {
            return false;
        }

        while (true) {
            int used = Volatile.Read(ref _used);
            if (used + chars > MaxChars) {
                return false;
            }

            if (Interlocked.CompareExchange(ref _used, used + chars, used) == used) {
                return true;
            }
        }
    }
}
