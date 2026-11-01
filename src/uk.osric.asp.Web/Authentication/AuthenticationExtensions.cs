// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using uk.osric.asp.Web.Users;

/// <summary>Registers the editor's local-account authentication and its protections.</summary>
internal static class AuthenticationExtensions {

    // Routine use renews the ticket; a month without a visit means signing in again.
    private static readonly TimeSpan LongTermWindow = TimeSpan.FromDays(30);

    /// <summary>Adds cookie authentication, authorisation, antiforgery, session and login throttling.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="environment">The host environment, which decides the cookie secure policy.</param>
    public static IServiceCollection AddEditorAuthentication(this IServiceCollection services, IWebHostEnvironment environment) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        _ = services.AddDataProtection().SetApplicationName("uk.osric.asp");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => {
                options.Cookie.Name = ".uk.osric.asp.auth";
                options.Cookie.HttpOnly = true;
                // Lax, so following a link from elsewhere still arrives signed in. Strict would not.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = SecurePolicy(environment);
                options.LoginPath = "/login";
                options.ReturnUrlParameter = "returnUrl";
                options.ExpireTimeSpan = LongTermWindow;
                options.SlidingExpiration = true;

                // An authorised account that is no longer valid is a 403, not a redirect loop.
                options.Events.OnRedirectToAccessDenied = context => {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };

                // Long-lived cookies need revocation: a password change or a removed account rotates
                // or deletes the stamp, and the next request with the old cookie is signed out.
                options.Events.OnValidatePrincipal = async context => {
                    string? userName = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    LocalUser? user = userName is null
                        ? null
                        : await context.HttpContext.RequestServices
                            .GetRequiredService<IUserStore>()
                            .FindAsync(userName, context.HttpContext.RequestAborted);
                    string? stamp = context.Principal?.FindFirstValue(EditorPolicy.SecurityStampClaim);

                    if (user is null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)) {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(EditorPolicy.Name, policy => policy.RequireAuthenticatedUser());

        services.AddAntiforgery(options => {
            options.Cookie.Name = ".uk.osric.asp.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = SecurePolicy(environment);
        });

        services.AddDistributedMemoryCache();
        services.AddSession(options => {
            options.Cookie.Name = ".uk.osric.asp.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = SecurePolicy(environment);
            options.IdleTimeout = TimeSpan.FromMinutes(20);
        });

        services.AddOptions<LoginRateLimitOptions>()
            .Validate(options => options.PermitLimit > 0, "The login permit limit must be positive.")
            .Validate(options => options.Window > TimeSpan.Zero, "The login window must be positive.");

        services.AddRateLimiter(options => {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(LoginRateLimitPolicy.Name, context => {
                LoginRateLimitOptions limit = context.RequestServices
                    .GetRequiredService<IOptions<LoginRateLimitOptions>>()
                    .Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: LoginRateLimitPolicy.Name,
                    factory: _ => new FixedWindowRateLimiterOptions {
                        PermitLimit = limit.PermitLimit,
                        Window = limit.Window,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    private static CookieSecurePolicy SecurePolicy(IWebHostEnvironment environment) {
        return environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    }
}
