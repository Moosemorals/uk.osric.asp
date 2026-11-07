// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Http;
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

        // The cookie carries a random key; the framework's own protected ticket lives in SQLite.
        // Sign-out and a rejected stamp delete the row; the sweep drops the rest.
        _ = services.AddHttpContextAccessor();
        services.AddSingleton<AuthTicketStore>();
        services.AddHostedService<AuthTicketCleanupService>();

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

                options.Events.OnRedirectToLogin = async context => {
                    SecurityHeaders.NoStore(context.HttpContext);
                    if (context.Request.Path.Value?.EndsWith("/events", StringComparison.Ordinal) == true
                        && context.Request.Path.StartsWithSegments("/assistant")) {
                        // The event stream is read by a script, so its refusal is a document it can parse.
                        await ErrorResponses.WriteJsonAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            ErrorMessage.ForStatus(StatusCodes.Status401Unauthorized, isAuthenticated: false));
                        return;
                    }
                    context.Response.Redirect(context.RedirectUri);
                };

                // An authorised account that is no longer valid is a page, not a redirect loop.
                options.Events.OnRedirectToAccessDenied = async context => {
                    SecurityHeaders.NoStore(context.HttpContext);
                    await ErrorResponses.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        ErrorMessage.ForStatus(StatusCodes.Status403Forbidden, isAuthenticated: true));
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

        // Registered after AddCookie, so the framework's post-configure has already built the
        // data-protected TicketDataFormat this wraps: serialisation and crypto stay the framework's.
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .PostConfigure<AuthTicketStore, IHttpContextAccessor, ILoggerFactory>((options, tickets, http, loggers) => {
                ISecureDataFormat<AuthenticationTicket> inner = options.TicketDataFormat
                    ?? throw new InvalidOperationException("Cookie authentication supplied no ticket format to wrap.");

                options.TicketDataFormat = new StoredTicketDataFormat(inner, tickets, http, options.Cookie.Name!);

                ILogger logger = loggers.CreateLogger(typeof(AuthenticationExtensions).FullName!);

                // ISecureDataFormat has no sign-out callback, so without this a stolen cookie would
                // outlive the browser's copy until it expired. The stamp still revokes on use.
                Func<CookieSigningOutContext, Task>? existing = options.Events.OnSigningOut;
                options.Events.OnSigningOut = async context => {
                    if (existing is not null) {
                        await existing(context);
                    }

                    string? key = context.Options.CookieManager.GetRequestCookie(context.HttpContext, context.Options.Cookie.Name!);
                    if (!string.IsNullOrEmpty(key)) {
                        try {
                            _ = tickets.Remove(key);
                        } catch (Exception exception) {
                            // The browser's cookie is still deleted below, but the row survives until
                            // its ticket expires, so a stolen copy would keep working.
                            logger.LogWarning(exception, "Could not delete the stored authentication ticket on sign-out.");
                        }
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
            // One header set owns this: SecurityHeaders sends DENY, which is stricter than the
            // SAMEORIGIN this would add when a token is generated.
            options.SuppressXFrameOptionsHeader = true;
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
            options.OnRejected = (context, _) => {
                SecurityHeaders.NoStore(context.HttpContext);
                return new ValueTask(ErrorResponses.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    ErrorMessage.ForStatus(429, context.HttpContext.User.Identity?.IsAuthenticated == true)));
            };

            options.AddPolicy(LoginRateLimitPolicy.Name, context => {
                LoginRateLimitOptions limit = context.RequestServices
                    .GetRequiredService<IOptions<LoginRateLimitOptions>>()
                    .Value;

                // Per client, so one address spending its attempts cannot deny sign-in to the
                // owner. Caddy writes X-Forwarded-For and the publish is loopback only, so the
                // address here is the browser's rather than the proxy's.
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: LoginRateLimitPolicy.PartitionFor(context.Connection.RemoteIpAddress),
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
