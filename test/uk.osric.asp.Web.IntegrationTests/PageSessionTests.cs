// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The per-visitor session through the real host and its real cookie.</summary>
[TestFixture]
public sealed partial class PageSessionTests : IntegrationTest {

    private const string Counter = """
        <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
           PageValue<int> stored = await session.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await session.SetAsync("visits", visits); %>
        <p>Visited <%= visits.ToString() %> times.</p>
        """;

    private const string Reader = """
        <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
           PageValue<int> stored = await session.TryGetValueAsync<int>("visits"); %>
        <p><%= stored.Found ? stored.Value.ToString() : "nothing" %></p>
        """;

    private const string Flash = """
        <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
           if (ctx.Request.Method == "POST") {
               await session.SetAsync("notice", "Your note was saved.");
               ctx.Redirect(ctx.Path, 303);
               return;
           }
           PageValue<string> notice = await session.TryGetValueAsync<string>("notice");
           if (notice.Found) {
               await session.DeleteAsync("notice");
               ctx.Write("<p>" + notice.Value! + "</p>");
           } %>
        <form method="post" class="form">
            <%== ctx.AntiforgeryField %>
            <button type="submit">Save</button>
        </form>
        """;

    private const string CookieName = PageSessionCookieOptions.DefaultName;

    [Test]
    public async Task APageThatWritesSetsTheCookieWithTheShellsFlagsAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/session-counter");

        string cookie = SetCookie(response);
        Assert.That(cookie, Does.Contain("httponly"), cookie);
        Assert.That(cookie, Does.Contain("samesite=strict"), cookie);
        Assert.That(cookie, Does.Contain("secure"), "Production marks every cookie Secure");
        Assert.That(cookie, Does.Not.Contain("expires"), "the session ends with the browser");
    }

    [Test]
    public async Task APageThatOnlyReadsSetsNoCookieAsync() {
        await SavePageAsync("/session-reader", Reader);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/session-reader");

        Assert.That(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies), Is.False,
            cookies is null ? "no cookie at all" : string.Join(", ", cookies));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("nothing"));
    }

    [Test]
    public async Task TwoClientsKeepSeparateValuesAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient first = CreateClient();
        using HttpClient second = CreateClient();

        _ = await first.GetStringAsync("/session-counter");
        _ = await first.GetStringAsync("/session-counter");
        string secondBody = await second.GetStringAsync("/session-counter");

        Assert.That(secondBody, Does.Contain("Visited 1 times."), "one visitor's count is not another's");
        Assert.That(await first.GetStringAsync("/session-counter"), Does.Contain("Visited 3 times."));
    }

    [Test]
    public async Task AClientPresentingAnUnknownKeyStartsEmptyAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient client = CreateClient(handleCookies: false);
        client.DefaultRequestHeaders.Add("Cookie", $"{CookieName}=AAAAAAAAAAAAAAAAAAAAAA");

        Assert.That(await client.GetStringAsync("/session-counter"), Does.Contain("Visited 1 times."));
    }

    [Test]
    public async Task SigningOutLeavesNoSessionBehindAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient client = await CreateSignedInClientAsync();
        _ = await client.GetStringAsync("/session-counter");
        int before = await CountSessionsAsync();
        Assert.That(before, Is.GreaterThan(0), "a write minted a session row");

        string token = await GetAntiforgeryTokenAsync(client, "/logout");
        HttpResponseMessage signedOut = await client.PostAsync("/logout", Form(("__RequestVerificationToken", token)));

        Assert.That(signedOut.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await CountSessionsAsync(), Is.EqualTo(before - 1), "the visitor's row is gone");
        Assert.That(SetCookieOrNull(signedOut), Does.Contain("expires"), "the browser is told to forget the cookie");
    }

    [Test]
    public async Task SigningInRotatesTheKeySoTheOldOneNoLongerResolvesAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient client = CreateClient();
        HttpResponseMessage written = await client.GetAsync("/session-counter");
        string? anonymousKey = CookieFrom(written);
        Assert.That(anonymousKey, Is.Not.Null, "the first write minted the cookie");
        int before = await CountSessionsAsync();

        await SignInAsync(client);

        Assert.That(await CountSessionsAsync(), Is.EqualTo(before), "the anonymous row went and an owner's row replaced it");
        Assert.That(await client.GetStringAsync("/session-counter"), Does.Contain("Visited 1 times."),
            "the signed-in visitor starts from nothing");

        using HttpClient stale = CreateClient(handleCookies: false);
        stale.DefaultRequestHeaders.Add("Cookie", $"{CookieName}={anonymousKey}");

        Assert.That(await stale.GetStringAsync("/session-counter"), Does.Contain("Visited 1 times."),
            "the old key names no session any more");
    }

    [Test]
    public async Task ASessionOfAnAccountIsNotInheritedByAnAnonymousVisitorAsync() {
        await SavePageAsync("/session-counter", Counter);
        using HttpClient owner = CreateClient();
        string token = await GetAntiforgeryTokenAsync(owner, "/login");
        HttpResponseMessage signIn = await owner.PostAsync("/login", Form(
            ("username", UserName),
            ("password", Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", token)));
        string? ownerKey = CookieFrom(signIn);
        Assert.That(ownerKey, Is.Not.Null, "signing in mints a session for the account");

        using HttpClient anonymous = CreateClient(handleCookies: false);
        anonymous.DefaultRequestHeaders.Add("Cookie", $"{CookieName}={ownerKey}");

        Assert.That(await anonymous.GetStringAsync("/session-counter"), Does.Contain("Visited 1 times."),
            "a stale owner key cannot hand its session to a stranger");
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    [TestCase("POST")]
    public async Task EveryStoredPageResponseIsNoStoreWithoutAValidatorAsync(string method) {
        await SavePageAsync("/session-plain", "Hello.");
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(new HttpMethod(method), "/session-plain");
        if (method == "POST") {
            request.Content = Form(("note", "hi"));
        }

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.Headers.CacheControl?.NoStore, Is.True, $"{method} must not be cached");
        Assert.That(response.Headers.ETag, Is.Null, $"{method} carries no validator");
    }

    [Test]
    public async Task IfNoneMatchIsIgnoredAsync() {
        await SavePageAsync("/session-plain", "Hello.");
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/session-plain");
        request.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue("\"anything\""));

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Hello."));
    }

    [Test]
    public async Task AnAnonymousPostShowsItsMessageOnceTooAsync() {
        await SavePageAsync("/session-flash-anonymous", Flash);
        using HttpClient client = CreateClient();
        string token = await GetAntiforgeryTokenAsync(client, "/session-flash-anonymous");

        HttpResponseMessage posted = await client.PostAsync(
            "/session-flash-anonymous", Form(("__RequestVerificationToken", token)));

        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await client.GetStringAsync("/session-flash-anonymous"), Does.Contain("Your note was saved."));
        Assert.That(await client.GetStringAsync("/session-flash-anonymous"), Does.Not.Contain("Your note was saved."));
    }

    [Test]
    public async Task ASaveAndRedirectShowsItsMessageOnceAsync() {
        await SavePageAsync("/session-flash", Flash);
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/session-flash");

        HttpResponseMessage posted = await client.PostAsync("/session-flash", Form(("__RequestVerificationToken", token)));
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(posted.Headers.Location?.ToString(), Is.EqualTo("/session-flash"));

        Assert.That(await client.GetStringAsync("/session-flash"), Does.Contain("Your note was saved."));
        Assert.That(await client.GetStringAsync("/session-flash"), Does.Not.Contain("Your note was saved."));
    }

    private async Task<int> CountSessionsAsync() => await WithServicesAsync(services =>
        services.GetRequiredService<StorageDbContext>().Set<PageSessionEntry>().CountAsync());

    // The value a response set a session cookie to, or null when it wrote none.
    private static string? CookieFrom(HttpResponseMessage response) {
        Match match = SessionCookie().Match(SetCookieOrNull(response));

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string SetCookie(HttpResponseMessage response) {
        string cookie = SetCookieOrNull(response);
        Assert.That(SessionCookie().IsMatch(cookie), Is.True, cookie);

        return cookie;
    }

    private static string SetCookieOrNull(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values.FirstOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal)) ?? string.Empty
            : string.Empty;

    [GeneratedRegex(@"^s=([A-Za-z0-9_-]{22})")]
    private static partial Regex SessionCookie();
}
