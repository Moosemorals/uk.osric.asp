// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using uk.osric.asp.Core.Pages;

/// <summary>The template allowance one request's whole layout chain, or one job, shares.</summary>
/// <remarks>
/// <para>
/// The limit names the render or the job rather than the layer, so a layout must not double the
/// allowance simply because there are two layers reading under it, and a worker that reads for nine
/// hours is refilled rather than starved (<c>pages-one-request-one-chain</c>). A read that would go
/// over is refused rather than clamped, because half a template is a misleading answer.
/// </para>
/// <para>
/// A read is whole, so the capacity is also the largest single template that can ever be read.
/// </para>
/// </remarks>
internal sealed class ReadBudget {

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private Allowance _allowance;

    /// <summary>Creates the allowance for one render or one job.</summary>
    /// <param name="capacity">The most one burst may spend, from the rate and the render's deadline.</param>
    /// <param name="perSecond">The rate the configuration sets.</param>
    /// <param name="clock">The clock a charge is measured against.</param>
    /// <param name="key">The configuration key that sets the rate, named in a refusal.</param>
    public ReadBudget(int capacity, double perSecond, TimeProvider clock, string key) {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _clock = clock;
        Key = key;
        _allowance = new Allowance(capacity, perSecond, clock.GetUtcNow());
    }

    /// <summary>The most one burst may spend, and so the largest single read.</summary>
    public int Capacity => _allowance.Capacity;

    /// <summary>The rate the configuration sets.</summary>
    public double PerSecond => _allowance.PerSecond;

    /// <summary>The configuration key that sets the rate.</summary>
    public string Key { get; }

    /// <summary>How many characters are available at this instant.</summary>
    public int Available {
        get {
            lock (_gate) {
                return _allowance.Available(_clock.GetUtcNow());
            }
        }
    }

    /// <summary>Charges one template against the budget.</summary>
    /// <param name="bytes">The template's length in UTF-8 bytes.</param>
    /// <returns>Whether the read fits; nothing is charged when it does not.</returns>
    public bool TryCharge(int bytes) {
        if (bytes < 0) {
            return false;
        }

        lock (_gate) {
            if (_allowance.TryTake(bytes, _clock.GetUtcNow(), out Allowance next)) {
                _allowance = next;
                return true;
            }

            return false;
        }
    }

    /// <summary>The sentence a refusal reads: the rate, the key and the burst.</summary>
    /// <returns>One sentence naming what was spent and which key sets it.</returns>
    public string DescribeRefusal() {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"more than {PerSecond:0.###} characters of page source per second ({Key}), {Capacity} at most in one burst");
    }
}
