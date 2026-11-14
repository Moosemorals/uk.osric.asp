// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>The fixed window a client address may spend sign-in attempts in.</summary>
/// <remarks>Read from <c>Authentication:Login</c>.</remarks>
internal sealed record LoginRateLimitOptions {

    /// <summary>Attempts allowed per window.</summary>
    public required int PermitLimit { get; init; }

    /// <summary>The length of the window.</summary>
    public required TimeSpan Window { get; init; }
}
