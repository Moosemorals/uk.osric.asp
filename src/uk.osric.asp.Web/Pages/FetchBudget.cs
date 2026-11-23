// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using uk.osric.asp.Core.Pages;

/// <summary>The fetches one render or one job may make, as a rate with a burst.</summary>
/// <remarks>
/// <para>
/// A render shares one of these across its whole chain, and a job holds one for its lifetime, so the
/// limit names the render or the job rather than the layer
/// (<c>pages-one-request-one-chain</c>). It is a bucket rather than a count because a job that
/// lives nine hours must not be given the allowance of a page that lives five seconds.
/// </para>
/// <para>
/// The figures are readable because a page cannot otherwise size a fan-out: the capability that
/// publishes them is <c>the-site-is-one-type</c>'s, and it can only publish what the object that
/// refuses knows.
/// </para>
/// </remarks>
internal sealed class FetchBudget {

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private Allowance _allowance;

    /// <summary>Creates the allowance for one render or one job.</summary>
    /// <param name="capacity">The most one burst may spend, from the rate and the render's deadline.</param>
    /// <param name="perSecond">The rate the configuration sets.</param>
    /// <param name="clock">The clock a take is measured against.</param>
    /// <param name="key">The configuration key that sets the rate, named in a refusal.</param>
    public FetchBudget(int capacity, double perSecond, TimeProvider clock, string key) {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _clock = clock;
        Key = key;
        _allowance = new Allowance(capacity, perSecond, clock.GetUtcNow());
    }

    /// <summary>The most one burst may spend.</summary>
    public int Capacity => _allowance.Capacity;

    /// <summary>The rate the configuration sets.</summary>
    public double PerSecond => _allowance.PerSecond;

    /// <summary>The configuration key that sets the rate.</summary>
    public string Key { get; }

    /// <summary>How many fetches are available at this instant.</summary>
    public int Available {
        get {
            lock (_gate) {
                return _allowance.Available(_clock.GetUtcNow());
            }
        }
    }

    /// <summary>Spends one fetch, when the render or job has one left.</summary>
    /// <returns><see langword="true"/> when the caller may make the request.</returns>
    public bool TryTake() {
        lock (_gate) {
            if (_allowance.TryTake(1, _clock.GetUtcNow(), out Allowance next)) {
                _allowance = next;
                return true;
            }

            return false;
        }
    }

    /// <summary>The line a refusal logs or a page reads: the rate, the key and the burst.</summary>
    /// <returns>One sentence naming what was spent and which key sets it.</returns>
    public string DescribeRefusal() {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"more than {PerSecond:0.###} fetches per second ({Key}), {Capacity} at most in one burst");
    }
}
