// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>What a render or a job may spend, as a rate with a burst.</summary>
/// <remarks>
/// <para>
/// It is a token bucket. The bucket starts full, so the capacity is what the first instant may spend
/// before anything is refilled, and it refills at <see cref="PerSecond"/> until it is full again. It
/// is a value and a function of the instant the caller passes, so nothing here reads a clock or takes
/// a lock, and a test drives it with two instants rather than a sleep
/// (<c>code-fixtures-hold-no-deadline</c>).
/// </para>
/// <para>
/// A take is all or nothing: a caller that asks for more than the capacity is refused however long it
/// waits, which is what keeps a half-read template from being rendered. A refusal answers
/// <see langword="false"/> and returns the allowance unchanged, so a refusal never costs.
/// </para>
/// </remarks>
public readonly record struct Allowance {

    private readonly double _units;
    private readonly DateTimeOffset _at;

    /// <summary>Creates an allowance that is full at <paramref name="fullAt"/>.</summary>
    /// <param name="capacity">The most one burst may spend, which must be at least one unit.</param>
    /// <param name="perSecond">How much is refilled each second, which must be positive.</param>
    /// <param name="fullAt">The instant the whole burst is available at.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The capacity is below one, or the rate is zero or negative, which would refuse every caller
    /// forever rather than bound one.
    /// </exception>
    public Allowance(int capacity, double perSecond, DateTimeOffset fullAt) {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);

        Capacity = capacity;
        PerSecond = perSecond;
        _units = capacity;
        _at = fullAt;
    }

    private Allowance(int capacity, double perSecond, double units, DateTimeOffset at) {
        Capacity = capacity;
        PerSecond = perSecond;
        _units = units;
        _at = at;
    }

    /// <summary>The most one burst may spend.</summary>
    public int Capacity { get; }

    /// <summary>How much is refilled each second.</summary>
    public double PerSecond { get; }

    /// <summary>Spends <paramref name="units"/> when they are there.</summary>
    /// <param name="units">How much to spend; zero spends nothing and is always allowed.</param>
    /// <param name="now">The instant to refill to before deciding.</param>
    /// <param name="next">The allowance after the take, or this one when the take was refused.</param>
    /// <returns><see langword="true"/> when the units were there and the spend happened.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="units"/> is negative.</exception>
    public bool TryTake(int units, DateTimeOffset now, out Allowance next) {
        ArgumentOutOfRangeException.ThrowIfNegative(units);

        double available = Refilled(now);
        if (units > available) {
            next = this;
            return false;
        }

        next = units == 0
            ? this
            : new Allowance(Capacity, PerSecond, available - units, now > _at ? now : _at);
        return true;
    }

    /// <summary>How many whole units <see cref="TryTake"/> would accept at <paramref name="now"/>.</summary>
    /// <param name="now">The instant to refill to before counting.</param>
    /// <returns>The whole units available, never more than <see cref="Capacity"/>.</returns>
    public int Available(DateTimeOffset now) => Math.Min(Capacity, (int)Math.Floor(Refilled(now)));

    // An instant before the last refill mints nothing and does not move the last instant: a correction
    // to the wall clock must not hand out a second burst.
    private double Refilled(DateTimeOffset now) {
        if (now <= _at) {
            return _units;
        }

        return Math.Min(Capacity, _units + (PerSecond * (now - _at).TotalSeconds));
    }
}
