// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The requests one render may still make, shared by every layer.</summary>
/// <remarks>
/// The cap names the render rather than the layer, so a layout must not double the allowance simply
/// because there are two layers rendering under it.
/// </remarks>
internal sealed class RenderFetchBudget {

    private readonly int _maxRequests;
    private int _used;

    /// <summary>Creates the counter for one render.</summary>
    /// <param name="maxRequests">How many requests the whole render may make.</param>
    public RenderFetchBudget(int maxRequests) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRequests, 1);

        _maxRequests = maxRequests;
    }

    /// <summary>Spends one request, when the render has one left.</summary>
    /// <returns><see langword="true"/> when the caller may make the request.</returns>
    public bool TryTake() {
        while (true) {
            int used = Volatile.Read(ref _used);
            if (used >= _maxRequests) {
                return false;
            }

            if (Interlocked.CompareExchange(ref _used, used + 1, used) == used) {
                return true;
            }
        }
    }
}
