// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Net;

/// <summary>Writes the one line a refused authentication leaves in the journal.</summary>
/// <remarks>
/// <para>
/// The wording is a contract with the host's ban: <c>deploy/fail2ban/filter.d/uk.osric.asp.conf</c>
/// matches <c>Authentication failed:</c> and the client's address straight after it, and an address
/// whose line appears too often is banned. Everything after the address is for the person reading
/// the journal, so it can say what was refused without making the line hard to match.
/// </para>
/// <para>
/// A request that ends unauthenticated and was refused is the failure, whoever refused it: the view
/// guard's <c>401</c>, a challenge that sends the browser to sign in, a page's own
/// <c>ctx.Response.RedirectToLogin()</c>. That is decided after the response is built, so the
/// middleware agrees with every producer instead of repeating the rule at each one.
/// </para>
/// </remarks>
internal static class AuthenticationFailures {

    // The sign-in's category: a refusal is part of the same story as a sign-in, and the journal keeps
    // one category per subject rather than one per route.
    private const string Category = "uk.osric.asp.Authentication";

    // A server that could not say who called leaves a line worth reading that no ban pattern matches.
    private const string UnknownAddress = "unknown";

    private const string SignInPath = "/login";

    /// <summary>Logs a request that ended unauthenticated and was refused.</summary>
    /// <param name="context">The request, before its response is written.</param>
    /// <param name="next">The rest of the pipeline, which decides the response.</param>
    public static async Task ApplyAsync(HttpContext context, RequestDelegate next) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        await next(context);

        if (ReasonFor(context) is { } reason) {
            Log(context, reason);
        }
    }

    /// <summary>Logs a refusal the request answers itself, such as a sign-in that renders its form again.</summary>
    /// <param name="context">The request being refused.</param>
    /// <param name="reason">What was refused, in a few words for the journal.</param>
    public static void Log(HttpContext context, string reason) {
        ArgumentNullException.ThrowIfNull(context);

        Logger(context).LogInformation(
            "Authentication failed: {ClientAddress} {Method} {Path} ({Reason})",
            Address(context), context.Request.Method, PathOf(context), reason);
    }

    /// <summary>Logs a refusal that has an account to name, which is a sign-in attempt.</summary>
    /// <param name="context">The request being refused.</param>
    /// <param name="reason">What was refused, in a few words for the journal.</param>
    /// <param name="account">What the request said it was signing in as.</param>
    public static void Log(HttpContext context, string reason, string account) {
        ArgumentNullException.ThrowIfNull(context);

        Logger(context).LogInformation(
            "Authentication failed: {ClientAddress} {Method} {Path} as {UserName} ({Reason})",
            Address(context), context.Request.Method, PathOf(context), account, reason);
    }

    // The response, read once the endpoints have had their say. A request that carries an identity is
    // not an authentication failure, however it is answered: a 403 is an account asking for something
    // it may not have, and a view token that tried to POST is authenticated and refused for its method.
    private static string? ReasonFor(HttpContext context) {
        if (context.User.Identity?.IsAuthenticated == true) {
            return null;
        }

        int status = context.Response.StatusCode;
        if (status == StatusCodes.Status401Unauthorized) {
            return "not authenticated";
        }

        bool redirect = status is StatusCodes.Status302Found or StatusCodes.Status303SeeOther
            or StatusCodes.Status307TemporaryRedirect or StatusCodes.Status308PermanentRedirect;
        if (redirect && SendsToSignIn(context)) {
            return "sent to sign in";
        }

        return null;
    }

    // The sign-in form's own path, with the return URL it is given; anything else redirecting is the
    // page's business and not a refusal.
    private static bool SendsToSignIn(HttpContext context) {
        string location = context.Response.Headers.Location.ToString();

        return string.Equals(location, SignInPath, StringComparison.Ordinal)
            || location.StartsWith(SignInPath + "?", StringComparison.Ordinal);
    }

    private static string PathOf(HttpContext context) => context.Request.Path.Value ?? "/";

    // Kestrel reports a dual-stack peer with its IPv4 address mapped into IPv6; the ban names the
    // address a firewall rule can hold, so the mapping is undone here.
    private static string Address(HttpContext context) {
        IPAddress? address = context.Connection.RemoteIpAddress;
        if (address is null) {
            return UnknownAddress;
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
}
