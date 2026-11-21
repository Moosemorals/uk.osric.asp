// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Tests;

/// <summary>A page reading the page store: one that lists it publicly, and the editor that guards it.</summary>
[TestFixture]
public sealed class PageCatalogTests : IntegrationTest {

    // The demo pages are fragments, so the fixture runs the host with the stored layout switched on.
    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    // The public listing the tests read. It is the whole of `ListAsync`: keys and save times, never a
    // template, and no guard, which is the capability's rule whether or not the site ships such a page.
    private const string Listing = """
<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Listing";
   ctx.Capabilities.GetRequired<IPageComposition>().Wrap(); %>
<h1>Listing</h1>
<table>
    <caption>Stored pages</caption>
    <thead>
        <tr>
            <th scope="col">Page</th>
            <th scope="col">Last saved</th>
        </tr>
    </thead>
    <tbody>
<% foreach (PageEntry entry in (await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(new())).Entries) { %>
        <tr>
            <th scope="row"><%= entry.Key %></th>
            <td><%= entry.SavedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) %></td>
        </tr>
<% } %>
    </tbody>
</table>
""";

    // A listing that asks for one slice and says whether anything stands after it, which is what a
    // caller walks a store of any size with. It carries the token a previous answer gave it, so a
    // second slice can say whether the list moved.
    private const string PagedListing = """
<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Paged";
   ctx.Capabilities.GetRequired<IPageComposition>().Wrap();
   string? after = ctx.Request.Query.TryGetValue("after", out string? asked) ? asked : null;
   string? version = ctx.Request.Query.TryGetValue("version", out string? carried) ? carried : null;
   string prefix = ctx.Request.Query.TryGetValue("prefix", out string? scope) && scope is not null ? scope : "";
   PageListing page = await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(
       new PageListingRequest(prefix, after, 2, version)); %>
<h1>Paged</h1>
<p id="more"><%= page.Truncated ? "more" : "end" %></p>
<p id="version"><%= page.Version %></p>
<p id="changed"><%= page.Changed ? "changed" : "unchanged" %></p>
<ul>
<% foreach (PageEntry entry in page.Entries) { %>
    <li><%= entry.Key %></li>
<% } %>
</ul>
""";

    private async Task SeedListingAsync() => await SavePageAsync("/catalog-listing", Listing);

    [Test]
    public async Task ASliceIsAsLongAsItWasAskedForAndSaysThereIsMoreAsync() {
        await SavePageAsync("/catalog-paged", PagedListing);
        using HttpClient client = CreateClient();

        string first = await client.GetStringAsync("/catalog-paged");
        string past = await client.GetStringAsync("/catalog-paged?after=/zzzz");

        Assert.Multiple(() => {
            Assert.That(first.Split("<li>").Length - 1, Is.EqualTo(2));
            Assert.That(first, Does.Contain("more"));
            Assert.That(past.Split("<li>").Length - 1, Is.Zero, "a cursor past the last key is a complete answer");
            Assert.That(past, Does.Contain("end"));
        });
    }

    [Test]
    public async Task APageCanTellWhetherItsListMovedBetweenSlicesAsync() {
        await SavePageAsync("/catalog-paged", PagedListing);
        await SavePageAsync("/catalog-tree", "root");
        await SavePageAsync("/catalog-tree/a", "a");
        await SavePageAsync("/catalog-tree/b", "b");
        await SavePageAsync("/catalog-tree/c", "c");
        using HttpClient client = CreateClient();

        string first = await client.GetStringAsync("/catalog-paged?prefix=/catalog-tree");
        string version = TextOf(first, "version");
        string last = KeysOf(first)[^1];
        string next = PagedUrl("/catalog-tree", last, version);

        string same = await client.GetStringAsync(next);
        await SavePageAsync("/catalog-other", "other");
        string elsewhere = await client.GetStringAsync(next);
        await SavePageAsync("/catalog-tree/d", "d");
        string moved = await client.GetStringAsync(next);

        Assert.Multiple(() => {
            Assert.That(version, Is.Not.Empty);
            Assert.That(TextOf(same, "changed"), Is.EqualTo("unchanged"));
            Assert.That(TextOf(elsewhere, "changed"), Is.EqualTo("unchanged"),
                "a save under another prefix does not move this one");
            Assert.That(TextOf(moved, "changed"), Is.EqualTo("changed"),
                "a save under the prefix turns the next slice into a warning");
        });
    }

    [Test]
    public async Task ThePublicListingNamesEveryPageAndWhenItChangedAsync() {
        await SeedListingAsync();
        await SavePageAsync("/catalog-listed", "<h1>Listed</h1>");
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/catalog-listing");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("/catalog-listed"));
        Assert.That(body, Does.Contain("/help"));
        Assert.That(body, Does.Contain("/_layout"));
        Assert.That(body, Does.Contain(" UTC"));
    }

    [Test]
    public async Task ThePublicListingIsServedToAnAnonymousVisitorAsync() {
        await SeedListingAsync();
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/catalog-listing");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("<h1>Listing</h1>"));
    }

    [Test]
    public async Task ThePublicListingNeverShowsSourceAsync() {
        const string marker = "CATALOG-UNIQUE-SOURCE-MARKER";
        await SeedListingAsync();
        await SavePageAsync("/catalog-secret", marker);
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/catalog-listing");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("/catalog-secret"));
        Assert.That(body, Does.Not.Contain(marker));
    }

    [Test]
    public async Task TheGuardedPageSendsAnAnonymousVisitorToSignInAsync() {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/pages?page=/help");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.ToString(), Does.StartWith("/login"));
    }

    [Test]
    public async Task TheGuardedPageListsTheStoreForItsOwnerAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("<h1>Pages</h1>"));
        Assert.That(body, Does.Contain("/help"));
        Assert.That(body, Does.Contain("href=\"/pages?page="));
    }

    [Test]
    public async Task TheGuardedPageShowsAPagesHistoryAndSourceAsync() {
        await SavePageAsync("/catalog-page", "<h1>Catalog page</h1>");
        PageTemplate page = (await ReadPageAsync("/catalog-page"))!;
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages?page=/catalog-page");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain(page.Hash));
        Assert.That(body, Does.Contain("&lt;h1&gt;Catalog page&lt;/h1&gt;"));
    }

    [Test]
    public async Task TheGuardedPageShowsOneRevisionByHashAsync() {
        await SavePageAsync("/catalog-rev", "old revision source");
        string oldHash = (await ReadPageAsync("/catalog-rev"))!.Hash;
        await SavePageAsync("/catalog-rev", "new revision source");
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync($"/pages?page=/catalog-rev&rev={oldHash}");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("old revision source"));
        Assert.That(body, Does.Not.Contain("new revision source"));
    }

    [Test]
    public async Task APageSourceIsEscapedInTheGuardedPageAsync() {
        await SavePageAsync("/catalog-script", "<script>alert('catalog')</script>");
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages?page=/catalog-script");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("<script>alert"));
        Assert.That(body, Does.Contain("&lt;script&gt;alert"));
    }

    [Test]
    public async Task BothDemoPagesAreWrappedInTheStoredLayoutAsync() {
        await SeedListingAsync();
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage recent = await anonymous.GetAsync("/catalog-listing");
        string recentBody = await recent.Content.ReadAsStringAsync();

        using HttpClient owner = await CreateSignedInClientAsync();
        using HttpResponseMessage pages = await owner.GetAsync("/pages");
        string pagesBody = await pages.Content.ReadAsStringAsync();

        Assert.That(recentBody, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(recentBody, Does.Contain("class=\"site-nav\""));
        Assert.That(pagesBody, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(pagesBody, Does.Contain("class=\"site-nav\""));
    }

    [Test]
    public async Task SavingAPageChangesWhatTheDemoPagesShowAsync() {
        await SeedListingAsync();
        await SavePageAsync("/catalog-fresh", "fresh source");
        using HttpClient anonymous = CreateClient();
        using HttpClient owner = await CreateSignedInClientAsync();

        using HttpResponseMessage recent = await anonymous.GetAsync("/catalog-listing");
        using HttpResponseMessage pages = await owner.GetAsync("/pages");

        Assert.That(await recent.Content.ReadAsStringAsync(), Does.Contain("/catalog-fresh"));
        Assert.That(await pages.Content.ReadAsStringAsync(), Does.Contain("/catalog-fresh"));
    }

    [Test]
    public async Task TheDemoPagesMeetTheAccessibilityRulesAsync() {
        await SeedListingAsync();
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage recent = await anonymous.GetAsync("/catalog-listing");
        AssertTableDocument(await recent.Content.ReadAsStringAsync(), withSource: false);

        using HttpClient owner = await CreateSignedInClientAsync();
        using HttpResponseMessage pages = await owner.GetAsync("/pages?page=/help");
        AssertTableDocument(await pages.Content.ReadAsStringAsync(), withSource: true);
    }

    private static string PagedUrl(string prefix, string after, string version) =>
        $"/catalog-paged?prefix={Uri.EscapeDataString(prefix)}"
        + $"&after={Uri.EscapeDataString(after)}&version={Uri.EscapeDataString(version)}";

    private static string TextOf(string body, string id) =>
        MarkupReader.Parse(body).Root.Descendants()
            .Single(element => element.Name == "p" && element.Attribute("id") == id)
            .Text;

    private static IReadOnlyList<string> KeysOf(string body) =>
        [.. MarkupReader.Parse(body).Root.Descendants()
            .Where(element => element.Name == "li")
            .Select(element => element.Text)
            .Where(text => text.StartsWith("/catalog-tree", StringComparison.Ordinal))];

    private static void AssertTableDocument(string body, bool withSource) {
        MarkupReader document = MarkupReader.Parse(body);

        Assert.That(document.Elements("h1"), Has.Count.EqualTo(1));
        Assert.That(document.Elements("table"), Has.Count.EqualTo(1));

        MarkupElement table = document.Elements("table")[0];
        Assert.That(table.Descendants().Where(element => element.Name == "th"), Is.Not.Empty);
        Assert.That(table.Descendants().Where(element => element.Name == "th").All(header => header.HasAttribute("scope")),
            "every heading cell is scoped");

        foreach (MarkupElement row in table.Descendants().Where(element => element.Name == "tr")) {
            bool hasHeading = row.Children.Any(child => child.Name == "th");
            bool hasCells = row.Children.Any(child => child.Name == "td");

            if (hasCells) {
                Assert.That(hasHeading, Is.True, "a row of cells must have a row heading");
            }
        }

        if (withSource) {
            MarkupElement source = document.Elements("pre").Single();
            Assert.That(source.Attribute("aria-label"), Is.Not.Null.And.Not.Empty);
            Assert.That(source.Descendants().Count(element => element.Name == "code"), Is.EqualTo(1));
        }
    }
}
