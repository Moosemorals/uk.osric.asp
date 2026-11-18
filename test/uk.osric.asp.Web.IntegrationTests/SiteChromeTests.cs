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
        <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "A fragment";
           ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %><h1>Fragment</h1>
        """;

    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

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

        Assert.That(items, Is.EqualTo(["Home", "Sign in"]));
    }

    [Test]
    public async Task ASignedInVisitorSeesTheToolsAndSignOut() {
        await SavePageAsync("/private-chrome", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        IReadOnlyList<string> items = await MenuAsync(client, "/private-chrome");

        Assert.That(items, Is.EqualTo(["Home", "Assistant", "Pages", "Admin", "Sign out"]));
    }

    [Test]
    public async Task TheSeededLayoutFoldsThePagesTitleSheetAndHead() {
        await SavePageAsync("/folded", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
               layout.Title = "Folded";
               layout.AddStylesheet("/folded.css");
               layout.AddHead("<meta name=\"folded\" content=\"1\">"); %><h1>Folded</h1>
            """);
        using HttpClient client = CreateClient();

        string body = await BodyAsync(client, "/folded");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("<title>Folded</title>"));
            Assert.That(body, Does.Contain("/folded.css"), "the page's sheet reaches the document");
            Assert.That(body, Does.Contain("<meta name=\"folded\" content=\"1\">"));
            Assert.That(body, Does.Contain("<h1>Folded</h1>"));
        });
    }

    // The two menus are written by different documents from one list, so they are compared on
    // everything a reader sees: where each item goes, what it says, and which one is the place shown.
    [Test]
    public async Task TheShellAndTheLayoutOfferTheSameItemsAndMarkTheirOwnPlace() {
        await SavePageAsync("/menu-compare", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        IReadOnlyList<MenuEntry> page = await MenuItemsAsync(client, "/menu-compare");
        IReadOnlyList<MenuEntry> shell = await MenuItemsAsync(client, "/admin/status");

        Assert.Multiple(() => {
            Assert.That(page.Select(item => item.Text), Is.EqualTo(shell.Select(item => item.Text)),
                "the seeded layout and the shell must not drift, in words or in order");
            Assert.That(Destinations(page, shell), Is.EqualTo(Destinations(shell, page)),
                "every item leads to the same place, whichever document marked it current");
            Assert.That(page.Where(item => item.Current), Is.Empty, "a stored page is no tool");
            Assert.That(shell.Where(item => item.Current).Select(item => item.Text), Is.EqualTo(["Admin"]));
        });
    }

    // The shell writes a different document from the stored layout on purpose, and the marker says
    // which one a reader is in: the shell's menu is the site's tools, not the pages the store holds.
    [Test]
    public async Task TheShellIsMarkedAndTheStoredLayoutIsNot() {
        using HttpClient client = CreateClient();

        string shell = await BodyAsync(client, "/login");
        string layout = await BodyAsync(client, "/about");

        Assert.Multiple(() => {
            Assert.That(shell, Does.Contain("<body class=\"shell\">"));
            Assert.That(shell, Does.Contain("<p class=\"shell-label\">Site tools</p>"));
            Assert.That(layout, Does.Not.Contain("<body class=\"shell\">"));
            Assert.That(layout, Does.Not.Contain("shell-label"));
        });
    }

    // A page is not limited to the menu the layout draws: the catalog gives it the links and it
    // writes whatever markup it likes, with no shell document and no layout around it.
    [Test]
    public async Task APageCanRenderTheShellsLinksItself() {
        await SavePageAsync("/own-menu", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Own menu";
               IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
               foreach (ShellLink link in catalog.ShellLinks()) { %><a href="<%= link.Href %>"><%= link.Text %></a><% } %>
            """);
        using HttpClient client = await CreateSignedInClientAsync();

        string body = await BodyAsync(client, "/own-menu");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("<a href=\"/assistant\">Assistant</a>"));
            Assert.That(body, Does.Contain("<a href=\"/admin\">Admin</a>"));
            Assert.That(body, Does.Contain("<a href=\"/help\">Help</a>"), "the administration section is the same list");
            Assert.That(body, Does.Not.Contain("aria-label=\"Site\""), "the page wrote its own markup");
        });
    }

    // The shell writes its own document and the seeded layout writes the library's, so a change to
    // one must not leave the other behind: the two are compared on everything a reader or a
    // stylesheet sees, not on the words in them.
    [Test]
    public async Task TheShellAndTheLayoutDrawTheSameDocument() {
        using HttpClient client = CreateClient();

        string shell = await BodyAsync(client, "/login");
        string layout = await BodyAsync(client, "/about");

        Assert.Multiple(() => {
            Assert.That(ShapeOf(layout), Is.EqualTo(ShapeOf(shell)), "the seeded document and the shell's have drifted");
            Assert.That(ShapeOf(layout).Sheets, Is.Not.Empty, "a document with no sheet proves nothing");
            Assert.That(ShapeOf(layout).Lang, Is.EqualTo("en"));
        });
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
        Assert.That(body, Does.Contain("href=\"/admin/status\""));
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
        Assert.That(body, Does.Contain("href=\"/pages?edit=%2Fhelp\""), "the manual links to its own editor, key escaped");
    }

    [Test]
    public async Task TheAboutPageIsPublicAndNamesItsCredits() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/about");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(body, Does.Contain("<title>About</title>"));
        Assert.That(body, Does.Contain("Osric Wilkinson"));
        Assert.That(body, Does.Contain("DeepSeek"));
        Assert.That(body, Does.Contain("href=\"/admin/copyright\""), "the licence texts are the shell's");

        if (BuildInfo.Current.Commit is { } commit) {
            Assert.That(body, Does.Contain($"https://github.com/Moosemorals/uk.osric.asp/commit/{commit}"));
        } else {
            Assert.That(body, Does.Contain("a build that does not record its commit"));
        }
    }

    [Test]
    public async Task TheEditorLinksToTheHelpPage() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/pages?edit=/help");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<a href=\"/help\">Help</a>"));
    }

    // The index is public: a visitor sees the tools that answer anyone and a line saying the rest
    // needs a sign-in, rather than being sent to the form to find out what is there.
    // The licence files are packed beside the assemblies, and the page reads them: a copy on /about
    // was a second home for a file that changes without the page noticing.
    [Test]
    public async Task TheCopyrightPageReadsTheFilesTheBuildCarries() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/copyright");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Copyright 2026 Osric Wilkinson"), "the site's LICENSE");
            Assert.That(body, Does.Contain("Acornima 1.9.1"), "the third-party notices");
            Assert.That(body, Does.Contain("id=\"licence-site\""), "the licence texts");
            Assert.That(body, Does.Contain("id=\"reuse-file\""), "the REUSE annotations");
        });
    }

    [Test]
    public async Task TheAdminIndexAnswersAVisitorAndHidesTheOwnersTools() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain(">Reference</a>"));
            Assert.That(body, Does.Not.Contain(">Import pages</a>"));
            Assert.That(body, Does.Contain("The rest of the administration is for the owner"));
        });
    }

    [Test]
    public async Task TheStatusDashboardIsReachableFromAdmin() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/admin/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("aria-label=\"Site\""));
    }

    [Test]
    public async Task TheOldStatusPathIsNotARoute() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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

        Assert.That(await MenuAsync(client, "/signed-out"), Is.EqualTo(["Home", "Sign in"]));
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentHomeFromTheSignOutView() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/logout");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/"));
    }

    [Test]
    public async Task APageThatSetsNoTitleIsServedInTheLayoutsOwnTitle() {
        await SavePageAsync("/untitled", """
            <% ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %><h1>Forgot</h1>
            """);
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/untitled");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Forgot</h1>"), "the wrapper decides what a missing title means");
        Assert.That(body, Does.Contain("<title>Site</title>"));
    }

    [Test]
    public async Task APageThatDeclaresNoWrapperIsServedAsTheFragmentItIs() {
        await SavePageAsync("/own-document", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Mine"; %><h1>Mine</h1>
            """);
        using HttpClient client = CreateClient();

        string body = await (await client.GetAsync("/own-document")).Content.ReadAsStringAsync();

        Assert.That(body.Trim(), Is.EqualTo("<h1>Mine</h1>"));
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

    // What a document says about itself, with the words in it and the title left out.
    // The lists are joined rather than held: a record compares a list by reference, not by value.
    private sealed record DocumentShape(
        string? Lang,
        string Sheets,
        string Icons,
        string? SkipLink,
        string? SkipText,
        string Landmarks,
        string? MainId);

    private static DocumentShape ShapeOf(string body) {
        Match skip = SkipLink().Match(body);

        return new DocumentShape(
            Lang().Match(body) is { Success: true } lang ? lang.Groups[1].Value : null,
            string.Join(' ', Sheets().Matches(body).Select(match => match.Groups[1].Value)),
            string.Join(' ', Icons().Matches(body).Select(match => match.Groups[1].Value)),
            skip.Success ? skip.Groups[1].Value : null,
            skip.Success ? skip.Groups[2].Value : null,
            string.Join(' ', Landmark().Matches(body).Select(match => match.Groups[1].Value)),
            Landmark().Matches(body)
                .Where(match => match.Groups[1].Value == "main")
                .Select(match => match.Groups[2].Value).SingleOrDefault());
    }

    private static async Task<string> BodyAsync(HttpClient client, string path) {
        HttpResponseMessage response = await client.GetAsync(path);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{path} answered {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("<html lang=\"([^\"]*)\"")]
    private static partial Regex Lang();

    [GeneratedRegex("<link rel=\"stylesheet\" href=\"([^\"]*)\"")]
    private static partial Regex Sheets();

    [GeneratedRegex("<link rel=\"icon\" href=\"([^\"]*)\"")]
    private static partial Regex Icons();

    [GeneratedRegex("<body[^>]*>\\s*<a class=\"skip-link\" href=\"([^\"]*)\">([^<]*)</a>")]
    private static partial Regex SkipLink();

    [GeneratedRegex("<(header|nav|main)(?: id=\"([^\"]*)\")?")]
    private static partial Regex Landmark();

    private sealed record MenuEntry(string Href, string Text, bool Current);

    // A current item is text, so its twin in the other document supplies the href it would have had.
    private static string[] Destinations(IReadOnlyList<MenuEntry> items, IReadOnlyList<MenuEntry> twin) =>
        [.. items.Select((item, index) => item.Current ? twin[index].Href : item.Href)];

    private static async Task<IReadOnlyList<MenuEntry>> MenuItemsAsync(HttpClient client, string path) {
        HttpResponseMessage response = await client.GetAsync(path);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{path} answered {(int)response.StatusCode}");

        string body = await response.Content.ReadAsStringAsync();
        Match nav = SiteMenu().Match(body);
        Assert.That(nav.Success, Is.True, $"{path} carries no site menu");

        return [.. MenuEntryPattern().Matches(nav.Groups[1].Value).Select(match => match.Groups[1].Success
            ? new MenuEntry(match.Groups[1].Value, WebUtility.HtmlDecode(match.Groups[2].Value), Current: false)
            : new MenuEntry(string.Empty, WebUtility.HtmlDecode(match.Groups[3].Value), Current: true))];
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

    [GeneratedRegex("(?:<a href=\"([^\"]*)\">([^<]*)</a>|<span class=\"current\" aria-current=\"true\">([^<]*)</span>)")]
    private static partial Regex MenuEntryPattern();
}
