// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed partial class SeedsEndpointTests : IntegrationTest {

    private const string Broken = "<% this is not valid C# %>";

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/seeds");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        });
    }

    [Test]
    public async Task APostWithoutAntiforgeryWritesNothingAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);

        HttpResponseMessage response = await client.PostAsync("/admin/seeds", new FormUrlEncodedContent([]));
        PageTemplate stored = (await ReadPageAsync("/pages"))!;

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(stored.Template, Is.EqualTo(Broken), "the refused POST restored nothing");
        });
    }

    [Test]
    public async Task OneTickBringsBackThePageTheEditorNeedsAndLeavesTheRestAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);
        await SavePageAsync("/about", "<h1>Mine</h1>");

        string token = await GetAntiforgeryTokenAsync(client, "/admin/seeds");
        List<KeyValuePair<string, string>> fields = await SeedFieldsAsync("/pages");
        fields.Add(new("__RequestVerificationToken", token));

        HttpResponseMessage restored = await client.PostAsync("/admin/seeds", new FormUrlEncodedContent(fields));
        string body = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");
        PageTemplate? storedPages = await ReadPageAsync("/pages");
        string? pagesSeed = await SeedSourceAsync("/pages");
        PageTemplate? about = await ReadPageAsync("/about");

        Assert.Multiple(() => {
            Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("/pages"));
            Assert.That(body, Does.Contain("1 written"));
            Assert.That(storedPages?.Template, Is.EqualTo(pagesSeed));
            Assert.That(about?.Template, Is.EqualTo("<h1>Mine</h1>"), "the unticked edit is untouched");
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the editor's page serves again");
        });
    }

    [Test]
    public async Task ThePageAndTheDiffWorkWhenTheSeedsTheyRecoverAreBrokenAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        await PostEditorAsync(client, "site.css", "* { display: none; }");
        await PostEditorAsync(client, "editor.css", "* { display: none; }");
        await SavePageAsync("/pages", Broken);
        await SavePageAsync("/_layout", Broken);

        HttpResponseMessage preview = await client.GetAsync("/admin/seeds");
        string previewBody = await preview.Content.ReadAsStringAsync();
        HttpResponseMessage diff = await client.GetAsync("/admin/seeds?diff=/pages");
        string diffBody = await diff.Content.ReadAsStringAsync();

        string token = await GetAntiforgeryTokenAsync(client, "/admin/seeds");
        List<KeyValuePair<string, string>> fields = await SeedFieldsAsync("/pages", "/_layout", "/site.css", "/editor.css");
        fields.Add(new("__RequestVerificationToken", token));
        HttpResponseMessage restored = await client.PostAsync("/admin/seeds", new FormUrlEncodedContent(fields));
        string restoredBody = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(previewBody, Does.Contain("<form method=\"post\" action=\"/admin/seeds\""));
            Assert.That(previewBody, Does.Not.Contain("<link"), "the recovery page loads no seeded asset");
            Assert.That(diff.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(diffBody, Does.Contain("What writing the seed of /pages would change"));
            Assert.That(diffBody, Does.Not.Contain("<link"));
            Assert.That(restoredBody, Does.Contain("4 written"));
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the editor's page serves again");
        });
    }

    [Test]
    public async Task AStoredRowAtAShellPathIsScrubbedAndTheShellFileStillServesAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/admin.css", "stray");

        HttpResponseMessage preview = await client.GetAsync("/admin/seeds");
        string previewBody = await preview.Content.ReadAsStringAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/admin/seeds");

        HttpResponseMessage scrubbed = await client.PostAsync("/admin/seeds", Form(
            ("__RequestVerificationToken", token),
            ("action", "scrub"),
            ("stray", "/admin.css"),
            ("confirm", "scrub")));
        string scrubBody = await scrubbed.Content.ReadAsStringAsync();
        HttpResponseMessage asset = await client.GetAsync("/admin.css");
        PageTemplate? stray = await ReadPageAsync("/admin.css");

        Assert.Multiple(() => {
            Assert.That(previewBody, Does.Contain("/admin.css"));
            Assert.That(previewBody, Does.Contain("Stored pages at shell paths"));
            Assert.That(scrubbed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(scrubBody, Does.Contain("1 removed"));
            Assert.That(stray, Is.Null);
            Assert.That(asset.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the shell's own file still answers");
        });
    }

    [Test]
    public async Task TheOldRecoveryRoutesAnswerNotFoundAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage restore = await client.GetAsync("/admin/restore");
        HttpResponseMessage apply = await client.GetAsync("/admin/seed-apply");

        Assert.Multiple(() => {
            Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(apply.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task TheButtonIsOfferedToAVisitorWhoHasNoAntiforgeryCookieAsync() {
        await SavePageAsync("/about", "<h1>Mine</h1>");

        // The antiforgery cookie is a session cookie and the sign-in cookie is not, so a browser that
        // has been restarted since the last sign-in presents one and not the other.
        using HttpClient client = CreateClient(handleCookies: false);
        Present(client, await SignInByHandAsync(client));

        HttpResponseMessage response = await client.GetAsync("/admin/seeds");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<button type=\"submit\">Apply the ticked seeds</button>"));
            Assert.That(body, Does.Contain("</html>"), "the document is not cut short");
        });
    }

    /// <summary>Signs in on a client with no cookie jar, returning only the sign-in cookie.</summary>
    private static async Task<string> SignInByHandAsync(HttpClient client) {
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

    private async Task<List<KeyValuePair<string, string>>> SeedFieldsAsync(params string[] keys) {
        return await WithServicesAsync(async services => {
            PageSeeds seeds = services.GetRequiredService<PageSeeds>();
            IPageStore store = services.GetRequiredService<IPageStore>();
            List<KeyValuePair<string, string>> fields = [new("action", "apply")];
            List<string> selected = [];

            foreach (string key in keys) {
                PagePath path = PagePath.Normalize(key);
                SeedFile seed = seeds.Find(path)
                    ?? throw new AssertionException($"{path.Key} is not a shipped seed");
                PageHeadSummary? head = await store.GetHeadAsync(path);
                int index = -1;
                for (int candidate = 0; candidate < seeds.Files.Count; candidate++) {
                    if (string.Equals(seeds.Files[candidate].Path.Key, path.Key, StringComparison.Ordinal)) {
                        index = candidate;
                        break;
                    }
                }

                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"no pack index for {path.Key}");
                string number = index.ToString(CultureInfo.InvariantCulture);
                string prefix = "was." + number;

                fields.Add(new(prefix + ".key", path.Key));
                fields.Add(new(prefix + ".deleted", head?.IsDeleted == true ? "1" : "0"));
                fields.Add(new(prefix + ".anchor", head?.SeedHash ?? string.Empty));
                fields.Add(new(prefix + ".content", head?.ContentHash ?? string.Empty));
                fields.Add(new(prefix + ".seed", seed.Hash));
                selected.Add(number);
            }

            foreach (string number in selected) {
                fields.Add(new("seed", number));
            }

            return fields;
        });
    }

    private Task<string?> SeedSourceAsync(string key) => WithServicesAsync(services =>
        Task.FromResult(services.GetRequiredService<PageSeeds>().Source(PagePath.Normalize(key))));

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}
