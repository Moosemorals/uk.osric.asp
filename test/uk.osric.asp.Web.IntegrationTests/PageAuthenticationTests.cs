// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Proves, through the real host, that a page can opt into the existing sign-in.</summary>
[TestFixture]
public sealed class PageAuthenticationTests : IntegrationTest {

    private const string GuardedPage = """
        <% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %>
        <h1>Draft</h1>
        <p>Signed in as <%= user.Name %>.</p>
        <form method="post" class="form">
            <%== ctx.AntiforgeryField %>
            <input id="note" name="note">
            <button type="submit">Save</button>
        </form>
        <% if (ctx.Request.Method == "POST" && ctx.Request.Form.TryGetValue("note", out string note)) { %>
        <p>Saved: <%= note %></p>
        <% } %>
        """;

    [Test]
    public async Task AnAnonymousVisitorToAGuardedPageIsSentToTheSignInForm() {
        await SavePageAsync("/private-signin", GuardedPage);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/private-signin");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(LocationOf(response), Does.StartWith("/login?returnUrl="));
        Assert.That(ReturnUrlOf(response), Is.EqualTo("/private-signin"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task SigningInReturnsToTheGuardedPageWithItsQuery() {
        await SavePageAsync("/private-round-trip", GuardedPage);
        using HttpClient client = CreateClient();

        HttpResponseMessage redirect = await client.GetAsync("/private-round-trip?tab=one&tab=two");
        string returnUrl = ReturnUrlOf(redirect);
        Assert.That(returnUrl, Is.EqualTo("/private-round-trip?tab=one&tab=two"), "a repeated parameter must survive");

        string token = await GetAntiforgeryTokenAsync(client, "/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        HttpResponseMessage signIn = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("returnUrl", returnUrl),
            ("__RequestVerificationToken", token)));

        Assert.That(signIn.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(signIn.Headers.Location?.ToString(), Is.EqualTo(returnUrl));

        HttpResponseMessage page = await client.GetAsync(returnUrl);
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain($"Signed in as {UserName}."));
        Assert.That(page.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(page.Headers.ETag, Is.Null);
    }

    [Test]
    public async Task AnAuthenticatedPostWithTheFieldIsAccepted() {
        await SavePageAsync("/private-form", GuardedPage);
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/private-form");

        HttpResponseMessage response = await client.PostAsync("/private-form", Form(
            ("note", "hello"),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Saved: hello"));
    }

    [Test]
    public async Task AnAuthenticatedPostWithoutTheFieldIsRefusedBeforeThePageRuns() {
        await SavePageAsync("/private-refused", """
            <% await ctx.Capabilities.GetRequired<IPageStorage>().SetAsync("ran", "yes"); %>
            <p>the page ran</p>
            """);
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.PostAsync("/private-refused", Form(("note", "hello")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("could not be verified"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);

        string? ran = await WithServicesAsync(services =>
            services.GetRequiredService<IPageStateStore>().GetValueAsync(PagePath.Normalize("/private-refused"), "ran"));
        Assert.That(ran, Is.Null, "a refused request must not execute the page");
    }

    [Test]
    public async Task SigningOutTurnsTheGuardedPageBackIntoARedirect() {
        await SavePageAsync("/private-signout", GuardedPage);
        using HttpClient client = await CreateSignedInClientAsync();
        Assert.That((await client.GetAsync("/private-signout")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string token = await GetAntiforgeryTokenAsync(client, "/private-signout");
        HttpResponseMessage logout = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));

        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        HttpResponseMessage after = await client.GetAsync("/private-signout");
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(ReturnUrlOf(after), Is.EqualTo("/private-signout"));
    }

    [Test]
    public async Task APasswordChangeMakesAnExistingCookieAnonymousForAPage() {
        const string name = "page-mutable";
        const string oldPassword = "page mutable one";
        const string newPassword = "page mutable two";
        await SeedAdditionalUserAsync(name, oldPassword);
        await SavePageAsync("/private-revoked", GuardedPage);
        using HttpClient client = await CreateSignedInClientAsync(name, oldPassword);
        Assert.That((await client.GetAsync("/private-revoked")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await ChangePasswordAsync(name, newPassword);

        HttpResponseMessage after = await client.GetAsync("/private-revoked");
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(LocationOf(after), Does.StartWith("/login"));
    }

    [Test]
    public async Task AnExternalReturnUrlStillFallsBackToTheEditor() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/login?returnUrl=//evil.example");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/edit"));
    }

    [Test]
    public async Task APublicPageStillRendersAnonymouslyButIsUncacheable() {
        await SavePageAsync("/public-anonymous", "Hello.");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/public-anonymous");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Hello."));
        Assert.That(response.Headers.ETag, Is.Null);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    // The page's redirect target is relative, so the raw header is used rather than Uri.PathAndQuery.
    private static string ReturnUrlOf(HttpResponseMessage response) {
        string location = LocationOf(response);
        Assert.That(location, Does.StartWith("/login?returnUrl="));

        return QueryHelpers.ParseQuery(location[location.IndexOf('?', StringComparison.Ordinal)..])["returnUrl"].ToString();
    }

    private static string LocationOf(HttpResponseMessage response) =>
        response.Headers.Location?.OriginalString ?? string.Empty;
}
