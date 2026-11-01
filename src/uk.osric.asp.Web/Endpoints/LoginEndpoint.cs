// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Endpoints;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Security.Claims;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Users;

internal static class LoginEndpoint {

    private const string RememberMeField = "rememberMe";

    /// <summary>Renders the sign-in form, or sends an authenticated visitor back to the editor.</summary>
    public static async Task GetAsync(HttpContext context, IAntiforgery antiforgery) {
        if (context.User.Identity?.IsAuthenticated == true) {
            Redirects.SeeOther(context, LocalRedirect.Safe(context.Request.Query["returnUrl"].ToString()));
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
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
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
                Redirects.SeeOther(context, LocalRedirect.Safe(returnUrl));
                return;
            }
        }

        await WriteFormAsync(context, antiforgery, returnUrl, userName, rememberMe, "Invalid username or password.");
    }

    /// <summary>Deletes the authentication cookie.</summary>
    public static async Task LogoutAsync(HttpContext context, IAuthenticationService authentication) {
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await authentication.SignOutAsync(
            context,
            CookieAuthenticationDefaults.AuthenticationScheme,
            new AuthenticationProperties());

        Redirects.SeeOther(context, "/");
    }

    private static async Task SignInAsync(
        HttpContext context,
        LocalUser user,
        bool rememberMe,
        IAuthenticationService authentication) {

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.NameIdentifier, user.UserName),
                new Claim(EditorPolicy.SecurityStampClaim, user.SecurityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

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

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(Framework.GetHeader("Sign in"));
        await context.Response.WriteAsync("<main><h1>Sign in</h1>");

        if (!string.IsNullOrEmpty(error)) {
            await context.Response.WriteAsync($"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>");
        }

        await context.Response.WriteAsync($"""
            <form method="post" action="/login">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl ?? string.Empty)}">
                <p><label>Username <input name="username" autocomplete="username" value="{WebUtility.HtmlEncode(userName ?? string.Empty)}"></label></p>
                <p><label>Password <input type="password" name="password" autocomplete="current-password"></label></p>
                <p><label><input type="checkbox" name="{RememberMeField}"{(rememberMe ? " checked" : "")}> Keep me signed in</label></p>
                <button type="submit">Sign in</button>
            </form>
            </main>
            """);
        await context.Response.WriteAsync(Framework.GetFooter());
    }
}
