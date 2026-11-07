// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Security.Claims;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Users;

/// <summary>Renders the sign-in form, accepts its POST and signs the visitor out.</summary>
internal static class LoginEndpoint {

    private const string RememberMeField = "rememberMe";

    /// <summary>Renders the sign-in form, or sends an authenticated visitor back to the editor.</summary>
    public static async Task GetAsync(HttpContext context, IAntiforgery antiforgery) {
        if (context.User.Identity?.IsAuthenticated == true) {
            NoStore(context);
            Redirect.SeeOther(context, LocalRedirect.Safe(context.Request.Query["returnUrl"].ToString()));
            return;
        }

        await WriteFormAsync(
            context,
            antiforgery,
            context.Request.Query["returnUrl"].ToString(),
            userName: null,
            rememberMe: true,
            error: null);
    }

    /// <summary>Checks the local account and starts a cookie session.</summary>
    public static async Task PostAsync(
        HttpContext context,
        IUserStore users,
        IPasswordService passwords,
        IAntiforgery antiforgery,
        IAuthenticationService authentication) {

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false } || !context.Request.HasFormContentType) {
            await WriteExpiredFormAsync(context, antiforgery);
            return;
        }

        IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
        string userName = form["username"].ToString();
        string password = form["password"].ToString();
        string? returnUrl = form["returnUrl"].ToString();
        bool rememberMe = form[RememberMeField].Count > 0;

        LocalUser? user = null;
        try {
            user = await users.FindAsync(UserName.Normalize(userName), context.RequestAborted);
        } catch (ArgumentException) {
            // An unusable name takes the same path as an unknown one.
        }

        if (user is null) {
            _ = passwords.VerifyDummy(password);
        } else {
            PasswordVerificationResult result = passwords.Verify(user, password);

            if (result == PasswordVerificationResult.SuccessRehashNeeded) {
                _ = await users.UpdateHashAsync(user.UserName, passwords.Hash(user, password), context.RequestAborted);
            }

            if (result != PasswordVerificationResult.Failed) {
                await SignInAsync(context, user, rememberMe, authentication);
                NoStore(context);
                Redirect.SeeOther(context, LocalRedirect.Safe(returnUrl));
                return;
            }
        }

        await WriteFormAsync(context, antiforgery, returnUrl, userName, rememberMe, "Invalid username or password.");
    }

    /// <summary>Deletes the authentication cookie.</summary>
    public static async Task LogoutAsync(HttpContext context, IAuthenticationService authentication) {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "auth.logout_expired",
                "Sign-out did not complete",
                "The sign-out form could not be verified, so you are still signed in. Go back to the page you came from and sign out again.",
                ErrorMessage.DefaultRemedies(isAuthenticated: true)));
            return;
        }

        await authentication.SignOutAsync(
            context,
            CookieAuthenticationDefaults.AuthenticationScheme,
            new AuthenticationProperties());

        Redirect.SeeOther(context, "/");
    }

    /// <summary>Re-renders the sign-in form after its token was rejected, with a fresh token.</summary>
    /// <param name="context">The rejected sign-in POST.</param>
    /// <param name="antiforgery">Mints the replacement token.</param>
    /// <remarks>
    /// A stale token can be answered before the endpoint runs, so the host's status-code fallback
    /// calls this too: a form that expired is a problem we can fix for the reader, not one to report
    /// at them.
    /// </remarks>
    public static Task WriteExpiredFormAsync(HttpContext context, IAntiforgery antiforgery) {
        NoStore(context);

        return WriteFormAsync(
            context,
            antiforgery,
            returnUrl: null,
            userName: null,
            rememberMe: true,
            error: "The sign-in form expired before it was submitted. Please sign in again.");
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static async Task SignInAsync(
        HttpContext context,
        LocalUser user,
        bool rememberMe,
        IAuthenticationService authentication) {

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserName),
                new Claim(EditorPolicy.SecurityStampClaim, user.SecurityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        StoredTicketDataFormat.MarkSignIn(context);

        await authentication.SignInAsync(
            context,
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = rememberMe });
    }

    private static async Task WriteFormAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        string? returnUrl,
        string? userName,
        bool rememberMe,
        string? error) {

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        bool failed = !string.IsNullOrEmpty(error);
        // Both fields hold the pair that failed, so both say so; the alert above is their description.
        string invalid = failed ? " aria-describedby=\"login-error\" aria-invalid=\"true\"" : string.Empty;

        await ShellDocument.BeginAsync(context, "Sign in");
        await context.Response.WriteAsync("<h1>Sign in</h1>");

        if (failed) {
            await context.Response.WriteAsync($"<p class=\"error\" id=\"login-error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</p>");
        }

        await context.Response.WriteAsync($"""
            <form method="post" action="/login" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl ?? string.Empty)}">
                <dl>
                    <dt><label for="username">Username</label></dt>
                    <dd><input id="username" name="username" autocomplete="username" value="{WebUtility.HtmlEncode(userName ?? string.Empty)}"{invalid}></dd>
                    <dt><label for="password">Password</label></dt>
                    <dd><input id="password" type="password" name="password" autocomplete="current-password"{invalid}></dd>
                    <dt><label for="{RememberMeField}">Keep me signed in</label></dt>
                    <dd><input id="{RememberMeField}" type="checkbox" name="{RememberMeField}"{(rememberMe ? " checked" : "")}></dd>
                </dl>
                <button type="submit">Sign in</button>
            </form>
            """);
        await ShellDocument.EndAsync(context);
    }
}
