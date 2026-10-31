// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Security.Claims;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Endpoints;
using uk.osric.asp.Web.Users;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class LoginEndpointTests {

    private const string Password = "correct horse battery staple";

    private readonly FakeUserStore _users = new();
    private readonly FakePasswordService _passwords = new();
    private readonly TestAntiforgery _antiforgery = new();
    private readonly RecordingAuthenticationService _authentication = new();

    [Test]
    public async Task GetRendersTheFormAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/login");

        await LoginEndpoint.GetAsync(context, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(body, Does.Contain("name=\"username\""));
        Assert.That(body, Does.Contain("name=\"password\""));
        Assert.That(body, Does.Contain("name=\"rememberMe\" checked"));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("value=\"request-token\""));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task GetRedirectsAnAuthenticatedUserAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/login");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], "test"));

        await LoginEndpoint.GetAsync(context, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit"));
    }

    [Test]
    public async Task PostWithInvalidAntiforgeryReturns400Async() {
        DefaultHttpContext context = FormContext("owner", Password);
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(_authentication.SignedInPrincipal, Is.Null);
    }

    [Test]
    public async Task PostWithANonFormBodyReturns400Async() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/login");

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task PostWithUnknownUserShowsTheGenericMessageAsync() {
        DefaultHttpContext context = FormContext("missing", Password);

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(body, Does.Contain("Invalid username or password."));
        Assert.That(_authentication.SignedInPrincipal, Is.Null);
    }

    [Test]
    public async Task PostWithTheWrongPasswordShowsTheGenericMessageAsync() {
        _users.Users["owner"] = User("owner", Password);
        DefaultHttpContext context = FormContext("owner", "the wrong password");

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Invalid username or password."));
        Assert.That(_authentication.SignedInPrincipal, Is.Null);
    }

    [Test]
    public async Task PostWithAnInvalidNameShowsTheGenericMessageAsync() {
        DefaultHttpContext context = FormContext("   ", Password);

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Invalid username or password."));
    }

    [Test]
    public async Task PostWithValidCredentialsSignsInAsync() {
        _users.Users["owner"] = User("owner", Password);
        DefaultHttpContext context = FormContext("Owner", Password, returnUrl: "/edit/page", rememberMe: true);

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/page"));
        Assert.That(_authentication.SignedInScheme, Is.EqualTo(CookieAuthenticationDefaults.AuthenticationScheme));
        Assert.That(_authentication.SignedInPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("owner"));
        Assert.That(_authentication.SignedInPrincipal?.FindFirstValue(EditorPolicy.SecurityStampClaim), Is.EqualTo("stamp"));
        Assert.That(_authentication.SignedInProperties?.IsPersistent, Is.True);
    }

    [Test]
    public async Task PostWithoutRememberMeUsesASessionCookieAsync() {
        _users.Users["owner"] = User("owner", Password);
        DefaultHttpContext context = FormContext("owner", Password, rememberMe: false);

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(_authentication.SignedInProperties?.IsPersistent, Is.False);
    }

    [Test]
    public async Task PostWithAnUnsafeReturnUrlFallsBackToTheEditorAsync() {
        _users.Users["owner"] = User("owner", Password);
        DefaultHttpContext context = FormContext("owner", Password, returnUrl: "https://evil");

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit"));
    }

    [Test]
    public async Task PostRehashesWhenTheHasherSaysSoAsync() {
        _users.Users["owner"] = User("owner", Password);
        _passwords.Result = Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded;
        DefaultHttpContext context = FormContext("owner", Password);

        await LoginEndpoint.PostAsync(context, _users, _passwords, _antiforgery, _authentication);

        Assert.That(_users.HashUpdates, Is.EqualTo(1));
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
    }

    [Test]
    public async Task LogoutSignsOutTheCookieAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/logout");

        await LoginEndpoint.LogoutAsync(context, _authentication);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/"));
        Assert.That(_authentication.SignedOutScheme, Is.EqualTo(CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static DefaultHttpContext FormContext(
        string userName,
        string password,
        string? returnUrl = null,
        bool rememberMe = true) {

        Dictionary<string, StringValues> fields = new() {
            ["username"] = userName,
            ["password"] = password,
        };

        if (returnUrl is not null) {
            fields["returnUrl"] = returnUrl;
        }

        if (rememberMe) {
            fields["rememberMe"] = "on";
        }

        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/login");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(fields);
        return context;
    }

    private static LocalUser User(string name, string password) {
        FakePasswordService passwords = new();

        return new LocalUser {
            UserName = name,
            PasswordHash = passwords.Hash(new LocalUser { UserName = name, PasswordHash = string.Empty, SecurityStamp = "stamp" }, password),
            SecurityStamp = "stamp",
        };
    }
}
