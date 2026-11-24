// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A rate with a burst, as the object that enforces it answers it.</summary>
/// <param name="Capacity">The most one burst may spend.</param>
/// <param name="Available">How many units are available at this instant.</param>
/// <param name="PerSecond">The rate the configuration sets.</param>
/// <param name="Key">The configuration key that sets the rate.</param>
/// <remarks>
/// One shape covers fetches, measured in requests, and template reads, measured in UTF-8 bytes,
/// because both enforcers answer the same four figures.
/// </remarks>
public sealed record SiteAllowance(int Capacity, int Available, double PerSecond, string Key);

/// <summary>A bounded count, as the object that enforces it answers it.</summary>
/// <param name="Capacity">The most one request or render may spend.</param>
/// <param name="Remaining">How much is left now.</param>
/// <param name="Key">The configuration key that sets the capacity.</param>
public sealed record SiteCounter(int Capacity, int Remaining, string Key);

/// <summary>The clock a render or a job is under.</summary>
/// <param name="Total">The whole budget, from the instant the clock was started.</param>
/// <param name="Remaining">What is left, never below zero.</param>
/// <param name="Key">The configuration key that sets the total.</param>
public sealed record SiteTime(TimeSpan Total, TimeSpan Remaining, string Key);

/// <summary>What one render or one job has left.</summary>
/// <param name="Fetches">The outbound-fetch allowance.</param>
/// <param name="Template">The template-read allowance.</param>
/// <param name="Body">The layer's body allowance, or <see langword="null"/> in a job.</param>
/// <param name="Embeds">The embeds the render may still make, or <see langword="null"/> in a job.</param>
/// <param name="Time">The clock the render or job is under.</param>
/// <remarks>
/// <para>
/// This is a snapshot, not a live view: read it again after a fan-out and the figures have moved.
/// Every figure is read from the object that would refuse the next attempt, so it cannot disagree
/// with a refusal.
/// </para>
/// <para>
/// <see cref="Body"/> is the allowance of the layer that asked, not a total for the chain: the
/// content's body, a wrapper's chrome and an embed's slice are different buffers, and a page is
/// charged to its own.
/// </para>
/// </remarks>
public sealed record SiteBounds(
    SiteAllowance Fetches,
    SiteAllowance Template,
    SiteCounter? Body,
    SiteCounter? Embeds,
    SiteTime Time);
