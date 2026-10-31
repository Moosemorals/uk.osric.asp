// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class EditorAuthenticationTests : IntegrationTest {

    [Test]
    public async Task AnonymousGetEditorRedirectsToLogin() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/edit/hello");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task AnonymousPostEditorWritesNothing() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/edit/anonymous", Form(("content", "hijacked")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(await ReadPageAsync("/anonymous"), Is.Null);
    }

    [Test]
    public async Task LoginPageRendersTheForm() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/login");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("name=\"username\""));
        Assert.That(body, Does.Contain("name=\"password\""));
        Assert.That(body, Does.Contain("name=\"rememberMe\" checked"));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
    }

    [Test]
    public async Task ValidCredentialsSignInAndRedirectToTheEditor() {
        using HttpClient client = CreateClient();
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/edit"));
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task InvalidCredentialsLeaveTheRequestSignedOut() {
        using HttpClient client = CreateClient();
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", "the wrong password"),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Invalid username or password."));
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task LoginWithoutAntiforgeryReturns400() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RememberMeCookieIsPersistent() {
        using HttpClient client = CreateClient();
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", token)));

        Assert.That(AuthCookie(response), Does.Contain("expires="));
    }

    [Test]
    public async Task WithoutRememberMeCookieIsASessionCookie() {
        using HttpClient client = CreateClient();
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("__RequestVerificationToken", token)));

        Assert.That(AuthCookie(response), Does.Not.Contain("expires="));
    }

    [Test]
    public async Task PasswordChangeInvalidatesAnExistingCookie() {
        const string name = "mutable";
        const string oldPassword = "mutable password one";
        const string newPassword = "mutable password two";
        await SeedAdditionalUserAsync(name, oldPassword);
        using HttpClient client = await CreateSignedInClientAsync(name, oldPassword);
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await ChangePasswordAsync(name, newPassword);

        HttpResponseMessage after = await client.GetAsync("/edit/hello");
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(after.Headers.Location?.PathAndQuery, Does.StartWith("/login"));
    }

    [Test]
    public async Task RemovedUserInvalidatesAnExistingCookie() {
        const string name = "removable";
        const string password = "removable password";
        await SeedAdditionalUserAsync(name, password);
        using HttpClient client = await CreateSignedInClientAsync(name, password);
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await RemoveUserAsync(name);

        HttpResponseMessage after = await client.GetAsync("/edit/hello");
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task SigningInElsewhereLeavesTheFirstSessionAlone() {
        using HttpClient first = await CreateSignedInClientAsync();
        using HttpClient second = await CreateSignedInClientAsync();

        Assert.That((await first.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await second.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LogoutClearsTheCookie() {
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/hello");

        HttpResponseMessage logout = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(logout.Headers.Location?.ToString(), Is.EqualTo("/"));
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    private static string AuthCookie(HttpResponseMessage response) {
        Assert.That(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values), Is.True, "the response set no cookies");

        string? cookie = values?.SingleOrDefault(value => value.StartsWith(".uk.osric.asp.auth=", StringComparison.Ordinal));
        Assert.That(cookie, Is.Not.Null, "the response did not set the authentication cookie");

        return cookie ?? string.Empty;
    }
}
