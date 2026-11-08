// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

[TestFixture]
public sealed class EditorAuthenticationTests : IntegrationTest {

    // Signing in is what this fixture proves, so it pays for the production hasher rather than the
    // fast double the other fixtures use.
    internal override AspApplication CreateApplication() {
        return new AspApplication(realPasswordHasher: true);
    }

    [Test]
    public async Task AnonymousGetEditorRedirectsToLogin() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/edit/hello");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task AnonymousPostEditorWritesNothing() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/edit/anonymous", Form(("template", "hijacked")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(await ReadPageAsync("/anonymous"), Is.Null);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
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
        // The antiforgery system owns these, so it does not have to override anything.
        Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
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
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
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
    public async Task LoginWithoutAntiforgeryRendersTheFormAgainAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password)));

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("The sign-in form expired"));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""), "the replacement form needs a fresh token");
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
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
    public async Task ARequestedPageWithAQuerySurvivesTheLoginRedirect() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/login?returnUrl=" + Uri.EscapeDataString("/edit/hello?tab=source"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/edit/hello?tab=source"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task LogoutClearsTheCookie() {
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/hello");

        HttpResponseMessage logout = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(logout.Headers.Location?.ToString(), Is.EqualTo("/"));
        Assert.That(logout.Headers.CacheControl?.NoStore, Is.True);
        Assert.That((await client.GetAsync("/edit/hello")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task SigningInStoresTheTicketAndSendsOnlyAKey() {
        using HttpClient client = CreateClient();
        int stored = await CountStoredTicketsAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(AuthCookieValue(response), Has.Length.LessThan(100), "the cookie should carry a random key, not the ticket");
        Assert.That(await CountStoredTicketsAsync(), Is.EqualTo(stored + 1));
    }

    [Test]
    public async Task SigningInAgainReplacesTheKey() {
        using HttpClient client = CreateClient();
        int stored = await CountStoredTicketsAsync();

        string firstToken = await GetAntiforgeryTokenAsync(client, "/login");
        HttpResponseMessage first = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", firstToken)));

        string secondToken = await GetAntiforgeryTokenAsync(client, "/edit/hello");
        HttpResponseMessage second = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", secondToken)));

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(AuthCookieValue(second), Is.Not.EqualTo(AuthCookieValue(first)));
        Assert.That(await CountStoredTicketsAsync(), Is.EqualTo(stored + 1), "the old row is replaced, not added to");
    }

    [Test]
    public async Task SigningOutDeletesTheStoredTicket() {
        using HttpClient client = await CreateSignedInClientAsync();
        int stored = await CountStoredTicketsAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit/hello");

        HttpResponseMessage logout = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await CountStoredTicketsAsync(), Is.EqualTo(stored - 1));
    }

    private static string AuthCookie(HttpResponseMessage response) {
        Assert.That(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values), Is.True, "the response set no cookies");

        string? cookie = values?.SingleOrDefault(value => value.StartsWith(".uk.osric.asp.auth=", StringComparison.Ordinal));
        Assert.That(cookie, Is.Not.Null, "the response did not set the authentication cookie");

        return cookie ?? string.Empty;
    }

    private static string AuthCookieValue(HttpResponseMessage response) {
        return AuthCookie(response)[".uk.osric.asp.auth=".Length..].Split(';')[0];
    }
}
