// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Tests;

/// <summary>The two seeded pages that demonstrate a page reading the page store.</summary>
[TestFixture]
public sealed class PageCatalogTests : IntegrationTest {

    // The demo pages are fragments, so the fixture runs the host with the stored layout switched on.
    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    [Test]
    public async Task ThePublicListingNamesEveryPageAndWhenItChangedAsync() {
        await SavePageAsync("/catalog-listed", "<h1>Listed</h1>");
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/recent");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("/catalog-listed"));
        Assert.That(body, Does.Contain("/help"));
        Assert.That(body, Does.Contain("/_layout"));
        Assert.That(body, Does.Contain(" UTC"));
    }

    [Test]
    public async Task ThePublicListingIsServedToAnAnonymousVisitorAsync() {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/recent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("<h1>Recent changes</h1>"));
    }

    [Test]
    public async Task ThePublicListingNeverShowsSourceAsync() {
        const string marker = "CATALOG-UNIQUE-SOURCE-MARKER";
        await SavePageAsync("/catalog-secret", marker);
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/recent");
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
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage recent = await anonymous.GetAsync("/recent");
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
        await SavePageAsync("/catalog-fresh", "fresh source");
        using HttpClient anonymous = CreateClient();
        using HttpClient owner = await CreateSignedInClientAsync();

        using HttpResponseMessage recent = await anonymous.GetAsync("/recent");
        using HttpResponseMessage pages = await owner.GetAsync("/pages");

        Assert.That(await recent.Content.ReadAsStringAsync(), Does.Contain("/catalog-fresh"));
        Assert.That(await pages.Content.ReadAsStringAsync(), Does.Contain("/catalog-fresh"));
    }

    [Test]
    public async Task TheDemoPagesMeetTheAccessibilityRulesAsync() {
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage recent = await anonymous.GetAsync("/recent");
        AssertTableDocument(await recent.Content.ReadAsStringAsync(), withSource: false);

        using HttpClient owner = await CreateSignedInClientAsync();
        using HttpResponseMessage pages = await owner.GetAsync("/pages?page=/help");
        AssertTableDocument(await pages.Content.ReadAsStringAsync(), withSource: true);
    }

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
