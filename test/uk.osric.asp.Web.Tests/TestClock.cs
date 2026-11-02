// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock : TimeProvider {

    /// <summary>The instant <see cref="GetUtcNow"/> returns.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => Now;
}
