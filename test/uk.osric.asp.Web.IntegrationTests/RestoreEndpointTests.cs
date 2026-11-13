// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed partial class RestoreEndpointTests : IntegrationTest {

    private const string Broken = "<% this is not valid C# %>";

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/restore");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        });
    }

    [Test]
    public async Task APostWithoutAntiforgeryWritesNothingAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);

        HttpResponseMessage response = await client.PostAsync("/admin/restore", new FormUrlEncodedContent([]));
        PageTemplate stored = (await ReadPageAsync("/pages"))!;

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(stored.Template, Is.EqualTo(Broken), "the refused POST restored nothing");
        });
    }

    [Test]
    public async Task TheRestorePageWorksWhenTheSeedsItRecoversAreBrokenAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        await PostEditorAsync(client, "site.css", "* { display: none; }");
        await PostEditorAsync(client, "admin.css", "* { display: none; }");
        await SavePageAsync("/pages", Broken);
        await SavePageAsync("/_layout", Broken);

        HttpResponseMessage preview = await client.GetAsync("/admin/restore");
        string previewBody = await preview.Content.ReadAsStringAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/admin/restore");
        HttpResponseMessage restored = await client.PostAsync(
            "/admin/restore", Form(("__RequestVerificationToken", token)));
        string restoredBody = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(previewBody, Does.Contain("<form method=\"post\" action=\"/admin/restore\""));
            Assert.That(previewBody, Does.Not.Contain("<link"), "the recovery page loads no seeded asset");
            Assert.That(restoredBody, Does.Contain("/site.css"));
            Assert.That(restoredBody, Does.Contain("/_layout"));
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the editor's page serves again");
        });
    }

    [Test]
    public async Task OnePostBringsBackThePageTheEditorNeedsAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);

        string token = await GetAntiforgeryTokenAsync(client, "/admin/restore");
        HttpResponseMessage restored = await client.PostAsync(
            "/admin/restore", Form(("__RequestVerificationToken", token)));
        string restoredBody = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");
        string body = await pages.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(restoredBody, Does.Contain("/pages"));
            Assert.That(restoredBody, Does.Contain("1 restored"));
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<h1>Pages</h1>"));
        });
    }

    [Test]
    public async Task TheButtonIsOfferedToAVisitorWhoHasNoAntiforgeryCookieAsync() {
        await SavePageAsync("/about", "<h1>Mine</h1>");

        // The antiforgery cookie is a session cookie and the sign-in cookie is not, so a browser that
        // has been restarted since the last sign-in presents one and not the other.
        using HttpClient client = CreateClient(handleCookies: false);
        Present(client, await SignInByHandAsync(client));

        HttpResponseMessage response = await client.GetAsync("/admin/restore");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<button type=\"submit\">Restore the seed</button>"));
            Assert.That(body, Does.Contain("</html>"), "the document is not cut short");
        });
    }

    /// <summary>Signs in on a client with no cookie jar, returning only the sign-in cookie.</summary>
    private async Task<string> SignInByHandAsync(HttpClient client) {
        HttpResponseMessage rendered = await client.GetAsync("/login");
        string form = await rendered.Content.ReadAsStringAsync();

        string antiforgery = CookieValue(rendered, ShellCookies.Antiforgery)
            ?? throw new AssertionException("the sign-in form sets an antiforgery cookie");

        Present(client, antiforgery);
        HttpResponseMessage signedIn = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", await TokenAsync(rendered))));
        Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther), form);

        return CookieValue(signedIn, ShellCookies.Authentication)
            ?? throw new AssertionException("signing in sets the sign-in cookie");
    }

    /// <summary>Presents one cookie, as a client with no jar of its own has to.</summary>
    private static void Present(HttpClient client, string cookie) {
        _ = client.DefaultRequestHeaders.Remove(HeaderNames.Cookie);
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, cookie);
    }

    /// <summary>Reads the token a form rendered from a response already in hand.</summary>
    private static async Task<string> TokenAsync(HttpResponseMessage rendered) {
        Match match = AntiforgeryTokenPattern().Match(await rendered.Content.ReadAsStringAsync());
        Assert.That(match.Success, Is.True, "the form carried no antiforgery token");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Picks one <c>Set-Cookie</c> out of a response, as a request would send it back.</summary>
    private static string? CookieValue(HttpResponseMessage response, string name) {
        foreach (string cookie in response.Headers.TryGetValues(HeaderNames.SetCookie, out IEnumerable<string>? values) ? values : []) {
            int end = cookie.IndexOf(';', StringComparison.Ordinal);
            string pair = end < 0 ? cookie : cookie[..end];
            if (pair.StartsWith(name + "=", StringComparison.Ordinal)) {
                return pair;
            }
        }

        return null;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}
