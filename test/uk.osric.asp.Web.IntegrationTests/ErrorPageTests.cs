// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>Proves the promise that no human is handed a blank failure, and no program is handed HTML.</summary>
[TestFixture]
public sealed class ErrorPageTests : IntegrationTest {

    [Test]
    public async Task AMissingPageIsADocumentWithAWayBackAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/no-such-page");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("<h1>Page not found</h1>"));
        Assert.That(body, Does.Contain("/no-such-page not found."));
        Assert.That(body, Does.Contain("Return to the home page"));
        Assert.That(body, Does.Contain("Error code: <code>page.not_found</code>"),
            "the document names the same code the problem document carries");
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task AnOwnerIsOfferedTheEditorForAMissingPageAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        string body = await (await client.GetAsync("/not-created-yet")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("href=\"/pages?edit=/not-created-yet\">Create this page"));
    }

    [Test]
    public async Task AnAnonymousVisitorIsNotOfferedTheEditorAsync() {
        using HttpClient client = CreateClient();

        string body = await (await client.GetAsync("/not-created-yet")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("href=\"/pages"));
        Assert.That(body, Does.Contain("Sign in"));
    }

    [Test]
    public async Task AnApiRequestGetsAProblemDocumentAsync() {
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/no-such-page");
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(body, Does.Contain("\"status\":404"));
        Assert.That(body, Does.Contain("\"code\":\"page.not_found\""));
        Assert.That(body, Does.Contain("\"detail\":\"/no-such-page not found.\""));
    }

    [Test]
    public async Task AStatusNoEndpointOwnedIsStillADocumentAsync() {
        // Routing answers an unsupported verb with 405 and no body, so this is the status-code
        // fallback rather than an endpoint's own page.
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Put, "/");

        HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("<h1>Not allowed</h1>"));
        Assert.That(body, Does.Contain("Return to the home page"));
    }

    [Test]
    public async Task AnUnknownEditorCommandIsADocumentAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/pages?edit=/anything&cmd=publish");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("did not name an action this page understands"));
        Assert.That(body, Does.Contain("Page list"));
        Assert.That(body, Does.Contain("Page list"), "the refusal offers the way back");
    }

    [Test]
    public async Task AnExpiredEditorFormIsADocumentThatSaysNothingWasSavedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.PostAsync("/pages?edit=/anything", Form(("template", "hijacked")));
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("nothing was saved"));
        Assert.That(body, Does.Contain("Go back"));
        Assert.That(body, Does.Contain("Reference:"));
    }
}
