// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using uk.osric.asp.Web.Configuration;

/// <summary>The sign-in policy: how long a cookie lasts and how hard a client may try.</summary>
/// <remarks>Every value comes from <c>Authentication</c>.</remarks>
internal sealed record AuthenticationOptions {

    /// <summary>How long a sign-in lasts without a visit before it is renewed or refused.</summary>
    public required TimeSpan TicketLifetime { get; init; }

    /// <summary>The login throttle.</summary>
    public required LoginRateLimitOptions Login { get; init; }

    /// <summary>Reads <c>Authentication</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static AuthenticationOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Authentication");
        AuthenticationOptions options = new() {
            TicketLifetime = TimeSpan.FromDays(reader.Number("TicketLifetimeDays", recommended: 30,
                minimum: 1, maximum: 365)),
            Login = new LoginRateLimitOptions {
                PermitLimit = reader.For("Login").Number("PermitLimit", recommended: 10,
                    minimum: 1, maximum: 1_000),
                Window = TimeSpan.FromSeconds(reader.For("Login").Number("WindowSeconds", recommended: 60,
                    minimum: 1, maximum: 3_600)),
            },
        };
        reader.Complete();
        return options;
    }
}
