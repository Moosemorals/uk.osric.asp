// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The layout, the menu and the tools, proved through the real host.</summary>
/// <remarks>
/// This fixture is the one that runs with the shipped <c>Pages:Layout</c>, because the rest are about
/// one page and should not have to write the chrome as well.
/// </remarks>
[TestFixture]
public sealed partial class SiteChromeTests : IntegrationTest {

    private const string Fragment = """
        <% ctx.Features.GetRequired<IPageLayout>().Title = "A fragment"; %><h1>Fragment</h1>
        """;

    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout", assistantEnabled: true);

    [Test]
    public async Task EveryPageIsWrappedInTheSeededLayout() {
        await SavePageAsync("/wrapped", Fragment);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/wrapped");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain("<title>A fragment</title>"));
        Assert.That(body, Does.Contain("<h1>Fragment</h1>"));
        Assert.That(body, Does.Contain("<main id=\"main\">"));
    }

    [Test]
    public async Task AnAnonymousVisitorSeesSignInAndNoTools() {
        await SavePageAsync("/public-chrome", Fragment);
        using HttpClient client = CreateClient();

        IReadOnlyList<string> items = await MenuAsync(client, "/public-chrome");

        Assert.That(items, Is.EqualTo((string[])["Home", "Sign in"]));
    }

    [Test]
    public async Task ASignedInVisitorSeesTheToolsAndSignOut() {
        await SavePageAsync("/private-chrome", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        IReadOnlyList<string> items = await MenuAsync(client, "/private-chrome");

        Assert.That(items, Is.EqualTo((string[])["Home", "Assistant", "Pages", "Admin", "Sign out"]));
    }

    [Test]
    public async Task TheShellAndTheLayoutOfferTheSameItems() {
        await SavePageAsync("/menu-compare", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        IReadOnlyList<string> page = await MenuAsync(client, "/menu-compare");
        IReadOnlyList<string> shell = await MenuAsync(client, "/edit");

        Assert.That(page, Is.EqualTo(shell), "the seeded layout and the shell must not drift");
    }

    [Test]
    public async Task TheAdminIndexIsAFragmentTheLayoutWraps() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/admin");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain("<h1>Administration</h1>"));
        Assert.That(body, Does.Contain("href=\"/admin/import\""));
        Assert.That(body, Does.Contain("href=\"/admin/export\""));
        Assert.That(body, Does.Contain("href=\"/status\""));
    }

    [Test]
    public async Task TheHelpPageIsWrappedInTheSeededLayout() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/help");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain("<title>Help</title>"));
        Assert.That(body, Does.Contain("<h1>Help</h1>"));
        Assert.That(body, Does.Contain("id=\"layout\"").And.Contain("id=\"keeping-current\""));
        Assert.That(body, Does.Contain("href=\"/edit/help\""));
    }

    [Test]
    public async Task TheEditorLinksToTheHelpPage() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/help");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<a href=\"/help\">Help</a>"));
    }

    [Test]
    public async Task TheAdminIndexRequiresSignIn() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Does.StartWith("/login?returnUrl="));
    }

    [Test]
    public async Task TheStatusDashboardIsReachableFromAdmin() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("aria-label=\"Site\""));
    }

    [Test]
    public async Task SigningOutFromTheMenuEndsTheSession() {
        await SavePageAsync("/signed-out", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage confirmation = await client.GetAsync("/logout");
        Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await confirmation.Content.ReadAsStringAsync(), Does.Contain("<h1>Sign out</h1>"));

        string token = await GetAntiforgeryTokenAsync(client, "/logout");
        HttpResponseMessage logout = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));
        Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        Assert.That(await MenuAsync(client, "/signed-out"), Is.EqualTo((string[])["Home", "Sign in"]));
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentHomeFromTheSignOutView() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/logout");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
    }

    [Test]
    public async Task ASignedInOwnerIsSentToTheEditorWhenThePageSetsNoTitle() {
        await SavePageAsync("/untitled", "<h1>Forgot</h1>");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/untitled");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(body, Does.Contain("<h1>The page set no title</h1>"));
        Assert.That(body, Does.Contain("href=\"/edit/untitled\">Edit this page"));
    }

    [Test]
    public async Task APageThatClearsItsLayoutIsServedUnwrapped() {
        await SavePageAsync("/own-document", """
            <% ctx.Features.GetRequired<IPageLayout>().Layout = null; %><!DOCTYPE html>
            <html lang="en"><head><title>Mine</title></head><body><h1>Mine</h1></body></html>
            """);
        using HttpClient client = CreateClient();

        string body = await (await client.GetAsync("/own-document")).Content.ReadAsStringAsync();

        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Not.Contain("layout.css"));
        Assert.That(body, Does.Not.Contain("<main id=\"main\">"));
    }

    [Test]
    public async Task TheLayoutIsAPageThatAnswersWhenAskedForByName() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/_layout");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.StartWith("<!DOCTYPE html>"));
    }

    [Test]
    public async Task AnAnonymousRenderSetsNoAntiforgeryCookie() {
        await SavePageAsync("/no-token", Fragment);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/no-token");

        Assert.That(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies), Is.False,
            cookies is null ? "no cookie at all" : string.Join(", ", cookies));
    }

    private static async Task<IReadOnlyList<string>> MenuAsync(HttpClient client, string path) {
        HttpResponseMessage response = await client.GetAsync(path);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{path} answered {(int)response.StatusCode}");

        string body = await response.Content.ReadAsStringAsync();
        Match nav = SiteMenu().Match(body);
        Assert.That(nav.Success, Is.True, $"{path} carries no site menu");

        return [.. MenuItem().Matches(nav.Groups[1].Value).Select(item => WebUtility.HtmlDecode(item.Groups[1].Value))];
    }

    [GeneratedRegex("<nav class=\"site-nav\" aria-label=\"Site\">(.*?)</nav>", RegexOptions.Singleline)]
    private static partial Regex SiteMenu();

    [GeneratedRegex("(?:<a [^>]*>|<span [^>]*>)([^<]*)<")]
    private static partial Regex MenuItem();
}
