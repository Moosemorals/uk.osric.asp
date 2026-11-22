// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary><c>ctx.Stores</c> through the real host, its session cookie and its browser cookie.</summary>
[TestFixture]
public sealed class PageStoresTests : IntegrationTest {

    private const string PageCounter = """
        <% PageValue<int> stored = await ctx.Stores.Page.Values.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await ctx.Stores.Page.Values.SetAsync("visits", visits); %>
        <p>Visited <%= visits.ToString() %> times.</p>
        """;

    private const string VisitorCounter = """
        <% PageValue<int> stored = await ctx.Stores.Visitor.Values.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await ctx.Stores.Visitor.Values.SetAsync("visits", visits); %>
        <p>Visited <%= visits.ToString() %> times.</p>
        """;

    private const string WritePerson = """
        <% await ctx.Stores.Visitor.Values.SetAsync("name", "Osric", StoreLifetime.Person); %>
        <p>written</p>
        """;

    private const string ReadPerson = """
        <% PageValue<string> name = await ctx.Stores.Visitor.Values.TryGetValueAsync<string>("/stores-person-write", "name"); %>
        <p><%= name.Found ? name.Value! : "nothing" %></p>
        """;

    private const string WriteSession = """
        <% await ctx.Stores.Visitor.Values.SetAsync("note", "kept", StoreLifetime.Session); %>
        <p>written</p>
        """;

    private const string ReadSession = """
        <% PageValue<string> note = await ctx.Stores.Visitor.Values.TryGetValueAsync<string>("/stores-session-write", "note"); %>
        <p><%= note.Found ? note.Value! : "nothing" %></p>
        """;

    private const string WriteBrowser = """
        <% await ctx.Stores.Visitor.Values.SetAsync("theme", "dark", StoreLifetime.Browser); %>
        <p>written</p>
        """;

    private const string ReadBrowser = """
        <% PageValue<string> theme = await ctx.Stores.Visitor.Values.TryGetValueAsync<string>("/stores-browser-write", "theme"); %>
        <p><%= theme.Found ? theme.Value! : "nothing" %></p>
        """;

    private const string StartSession = """
        <% await ctx.Stores.Visitor.Values.SetAsync("started", true, StoreLifetime.Session); %>started
        """;

    private const string WriteNotice = """
        <% await ctx.Stores.Visitor.Values.SetAsync("notice", "Saved.", StoreLifetime.NextRead); %>
        <p>written</p>
        """;

    private const string TakeNotice = """
        <% PageValue<string> notice = await ctx.Stores.Visitor.Values.TryTakeAsync<string>("notice"); %>
        <p><%= notice.Found ? notice.Value! : "nothing" %></p>
        """;

    [Test]
    public async Task ThePageSubjectIsSharedByEveryVisitorAsync() {
        await SavePageAsync("/stores-page", PageCounter);
        using HttpClient first = CreateClient();
        using HttpClient second = CreateClient();

        Assert.That(await first.GetStringAsync("/stores-page"), Does.Contain("Visited 1 times."));
        Assert.That(await first.GetStringAsync("/stores-page"), Does.Contain("Visited 2 times."));
        Assert.That(await second.GetStringAsync("/stores-page"), Does.Contain("Visited 3 times."),
            "the page's own values are the page's, not the reader's");
    }

    [Test]
    public async Task TwoVisitorsKeepTheirOwnValuesAsync() {
        await SavePageAsync("/stores-visitor", VisitorCounter);
        using HttpClient first = CreateClient();
        using HttpClient second = CreateClient();

        _ = await first.GetStringAsync("/stores-visitor");
        Assert.That(await first.GetStringAsync("/stores-visitor"), Does.Contain("Visited 2 times."));
        Assert.That(await second.GetStringAsync("/stores-visitor"), Does.Contain("Visited 1 times."));
    }

    [Test]
    public async Task APersonsValueFollowsThemToTheirAccountAsync() {
        await SavePageAsync("/stores-person-write", WritePerson);
        await SavePageAsync("/stores-person-read", ReadPerson);
        using HttpClient client = CreateClient();
        _ = await client.GetStringAsync("/stores-person-write");
        Assert.That(await client.GetStringAsync("/stores-person-read"), Does.Contain("Osric"));

        await SignInAsync(client);

        Assert.That(await client.GetStringAsync("/stores-person-read"), Does.Contain("Osric"),
            "signing in adopts the session's Person values into the account");
    }

    [Test]
    public async Task ASessionValueDoesNotFollowThemToTheirAccountAsync() {
        await SavePageAsync("/stores-session-write", WriteSession);
        await SavePageAsync("/stores-session-read", ReadSession);
        using HttpClient client = CreateClient();
        _ = await client.GetStringAsync("/stores-session-write");
        Assert.That(await client.GetStringAsync("/stores-session-read"), Does.Contain("kept"));

        await SignInAsync(client);

        Assert.That(await client.GetStringAsync("/stores-session-read"), Does.Contain("nothing"),
            "a Session value dies with the session that held it");
    }

    [Test]
    public async Task ABrowsersValueSurvivesANewSessionAsync() {
        await SavePageAsync("/stores-browser-write", WriteBrowser);
        await SavePageAsync("/stores-browser-read", ReadBrowser);
        using HttpClient client = CreateClient(handleCookies: false);
        HttpResponseMessage written = await client.GetAsync("/stores-browser-write");
        string browser = CookieFrom(written, "b");
        Assert.That(browser, Is.Not.Empty, "a Browser write mints the browser cookie");

        // The session cookie is left behind, as a visitor who cleared it would: only the browser's own
        // cookie comes back, so the value has to be reached through the browser subject.
        using HttpClient later = CreateClient(handleCookies: false);
        later.DefaultRequestHeaders.Add("Cookie", $"b={browser}");

        Assert.That(await later.GetStringAsync("/stores-browser-read"), Does.Contain("dark"));
    }

    [Test]
    public async Task AOneShotValueIsTakenOnceAsync() {
        await SavePageAsync("/stores-notice-write", WriteNotice);
        await SavePageAsync("/stores-notice-take", TakeNotice);
        using HttpClient client = CreateClient();
        _ = await client.GetStringAsync("/stores-notice-write");

        Assert.That(await client.GetStringAsync("/stores-notice-take"), Does.Contain("Saved."));
        Assert.That(await client.GetStringAsync("/stores-notice-take"), Does.Contain("nothing"));
    }

    [Test]
    public async Task ABrowserValueIsNotVisibleInAnotherBrowserAsync() {
        await SavePageAsync("/stores-browser-write", WriteBrowser);
        await SavePageAsync("/stores-browser-read", ReadBrowser);
        using HttpClient client = CreateClient(handleCookies: false);
        HttpResponseMessage written = await client.GetAsync("/stores-browser-write");
        string browser = CookieFrom(written, "b");
        _ = await client.GetStringAsync("/stores-browser-read");

        using HttpClient stranger = CreateClient();
        Assert.That(await stranger.GetStringAsync("/stores-browser-read"), Does.Contain("nothing"));
    }

    // The value a response set a named cookie to, or an empty string when it wrote none.
    // The seeded toggle writes a Browser value now, and a visitor who chose before that is still
    // holding a Session value for the same key: the page reads theme-first, so the toggle has to
    // clear the old one or the choice lands a visit late.
    [Test]
    public async Task TheEditorToggleClearsAValueWrittenBeforeThereWasABrowserSubjectAsync() {
        await SavePageAsync("/stores-session-start", StartSession);
        using HttpClient client = CreateClient();
        HttpResponseMessage started = await client.GetAsync("/stores-session-start");
        string session = CookieFrom(started, "s");
        Assert.That(session, Is.Not.Empty, "a Session write mints the cookie");

        await WithServicesAsync(async services => {
            PageStoresOptions stores = services.GetRequiredService<PageStoresOptions>();
            IStoreValues values = services.GetRequiredService<IStoreValues>();
            await values.WriteAsync(
                new StoreValueWrite($"session:{session}", "/editor-mode", "vim",
                    (int)StoreLifetimeKind.Session, null, "\"plain\"", RequireLivePage: false),
                stores.Visitor.Anonymous,
                CancellationToken.None);
            return true;
        });

        // The stale session row is the one a read answers, so this visit is offered a plain box.
        string token = await GetAntiforgeryTokenAsync(client, "/editor-mode");
        HttpResponseMessage posted = await client.PostAsync("/editor-mode", Form(
            ("mode", "vim"),
            ("to", "/"),
            ("__RequestVerificationToken", token)));
        Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        Assert.That(await client.GetStringAsync("/editor-mode"), Does.Contain("the vim editor"));

        int leftover = await WithServicesAsync(services => services.GetRequiredService<StorageDbContext>()
            .Set<StoreValueEntry>()
            .Where(row => row.Subject == "session:" + session && row.PageKey == "/editor-mode")
            .CountAsync());
        Assert.That(leftover, Is.Zero, "the old spelling of the choice is gone");
    }

    private static string CookieFrom(HttpResponseMessage response, string name) {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)) {
            return string.Empty;
        }

        string prefix = name + "=";
        string? cookie = values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (cookie is null) {
            return string.Empty;
        }

        int end = cookie.IndexOf(';', StringComparison.Ordinal);

        return cookie[prefix.Length..(end < 0 ? cookie.Length : end)];
    }
}
