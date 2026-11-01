// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>The fixed window applied to sign-in attempts.</summary>
internal sealed class LoginRateLimitOptions {

    /// <summary>Attempts allowed in one window.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>The window length.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
