// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>Names the rate-limiting policy applied to the sign-in POST.</summary>
public static class LoginRateLimitPolicy {

    /// <summary>The policy name used by routing and the limiter registration.</summary>
    public const string Name = "login";
}
