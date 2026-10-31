// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class EditorEndpointTests : IntegrationTest {

    [Test]
    public async Task EditorRendersTheStoredSource() {
        await SavePageAsync("/hello", "Hello, world!");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/edit/hello");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("name=\"content\""));
        Assert.That(body, Does.Contain(">Hello, world!</textarea>"));
    }

    [Test]
    public async Task EditorRendersAnEmptyFormForANewPage() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/edit/new-page");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("name=\"content\""));
        Assert.That(body, Does.Contain("></textarea>"));
    }

    [Test]
    public async Task SaveRedirectsToTheEditorAndStoresTheSource() {
        using HttpClient client = CreateClient();

        HttpResponseMessage save = await client.PostAsync("/edit/hello", Form("Saved, <% ctx.Write(\"world\"); %>!"));

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.Found));
        Assert.That(save.Headers.Location?.ToString(), Is.EqualTo("/edit/hello"));

        HttpResponseMessage page = await client.GetAsync("/hello");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Saved, world!"));
    }

    [Test]
    public async Task SavedMessageIsShownOnTheNextRequestOnly() {
        using HttpClient client = CreateClient();

        await client.PostAsync("/edit/flash", Form("Hello."));

        HttpResponseMessage confirmation = await client.GetAsync("/edit/flash");
        Assert.That(await confirmation.Content.ReadAsStringAsync(), Does.Contain("<p>Saved successfully.</p>"));

        HttpResponseMessage next = await client.GetAsync("/edit/flash");
        Assert.That(await next.Content.ReadAsStringAsync(), Does.Not.Contain("Saved successfully."));
    }

    [Test]
    public async Task SaveWithoutContentReportsValidationAndWritesNothing() {
        using HttpClient client = CreateClient();

        await client.PostAsync("/edit/required", Form(null));

        HttpResponseMessage editor = await client.GetAsync("/edit/required");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("Content is required."));

        HttpResponseMessage page = await client.GetAsync("/required");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SavingAReservedKeyIsRefused() {
        using HttpClient client = CreateClient();

        await client.PostAsync("/edit/site.css", Form("hijacked"));

        HttpResponseMessage editor = await client.GetAsync("/edit/site.css");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("/site.css is reserved by the site."));

        HttpResponseMessage page = await client.GetAsync("/site.css");
        Assert.That(page.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
    }

    private static FormUrlEncodedContent Form(string? content) {
        Dictionary<string, string> fields = [];
        if (content is not null) {
            fields["content"] = content;
        }

        return new FormUrlEncodedContent(fields);
    }
}
