// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

/// <summary>A clock the test moves by hand, for wall time and for a monotonic duration.</summary>
internal sealed class TestClock : TimeProvider {

    private long _timestamp;

    /// <summary>The instant <see cref="GetUtcNow"/> returns.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>One tick of the duration clock is one tick of wall time, which makes advancing readable.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    /// <summary>Moves the duration clock forward, so a scan can be run out of its allowance.</summary>
    /// <param name="duration">How far to move it.</param>
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}
