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
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/hello");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("name=\"content\""));
        Assert.That(body, Does.Contain(">Hello, world!</textarea>"));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task EditorRendersAnEmptyFormForANewPage() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/new-page");

        string body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("name=\"content\""));
        Assert.That(body, Does.Contain("></textarea>"));
    }

    [Test]
    public async Task SaveRedirectsToTheEditorAndStoresTheSource() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "hello", "Saved, <% ctx.Write(\"world\"); %>!");

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(save.Headers.Location?.ToString(), Is.EqualTo("/edit/hello"));

        HttpResponseMessage page = await client.GetAsync("/hello");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("Saved, world!"));
    }

    [Test]
    public async Task SaveWithoutATokenReturns400AndWritesNothing() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await client.PostAsync("/edit/no-token", Form(("content", "hijacked")));

        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        HttpResponseMessage page = await client.GetAsync("/no-token");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SavedMessageIsShownOnTheNextRequestOnly() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "flash", "Hello.");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage confirmation = await client.GetAsync("/edit/flash");
        Assert.That(await confirmation.Content.ReadAsStringAsync(), Does.Contain("<p>Saved successfully.</p>"));

        HttpResponseMessage next = await client.GetAsync("/edit/flash");
        Assert.That(await next.Content.ReadAsStringAsync(), Does.Not.Contain("Saved successfully."));
    }

    [Test]
    public async Task SaveWithoutContentReportsValidationAndWritesNothing() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "required", null);
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage editor = await client.GetAsync("/edit/required");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("Content is required."));

        HttpResponseMessage page = await client.GetAsync("/required");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SavingAReservedKeyIsRefused() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "site.css", "hijacked");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        HttpResponseMessage editor = await client.GetAsync("/edit/site.css");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Contain("/site.css is reserved by the site."));

        HttpResponseMessage page = await client.GetAsync("/site.css");
        Assert.That(page.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
    }
}
