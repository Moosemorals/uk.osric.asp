// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class ErrorHandlingTests : IntegrationTest {

    private const string ThrowingPage = "<% throw new System.InvalidOperationException(\"Boom.\"); %>";

    [Test]
    public async Task ThrowingGetIsAnsweredByTheErrorPage() {
        await SavePageAsync("/throw", ThrowingPage);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/throw");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(body, Does.Contain("Something went wrong").And.Contain("An unexpected error occurred."));
        Assert.That(body, Does.Not.Contain("Boom."), "the exception message must not leak to the client");
    }

    [Test]
    public async Task ThrowingPageShowsTemplateDetailsToSignedInEditorAsync() {
        await SavePageAsync("/throw", "line one\nline two\n<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        using HttpClient client = await CreateSignedInClientAsync();
        int compileCount = CompileCount;

        HttpResponseMessage response = await client.GetAsync("/throw");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(body, Does.Contain("InvalidOperationException").And.Contain("Boom."));
        Assert.That(body, Does.Contain("at Handle(IContext ctx) in /throw:line 3"));
        Assert.That(body, Does.Not.Contain("/throw:3:4"));
        Assert.That(body, Does.Not.Contain("GeneratedPage.Handle"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(CompileCount, Is.EqualTo(compileCount + 1));

        HttpResponseMessage cachedResponse = await client.GetAsync("/throw");
        string cachedBody = await cachedResponse.Content.ReadAsStringAsync();
        Assert.That(cachedBody, Does.Contain("at Handle(IContext ctx) in /throw:line 3"));
        Assert.That(CompileCount, Is.EqualTo(compileCount + 1), "the stored image and symbols should be reused together");
    }

    [Test]
    public async Task ThrowingPostIsAnsweredByTheErrorPage() {
        // The page route accepts every verb, so this reproduces BUG-01: a failed POST used to fall
        // through the router and be answered with a 404 instead of the error page.
        await SavePageAsync("/throw", ThrowingPage);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/throw", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Something went wrong"));
    }

    [Test]
    public async Task ErrorRouteIsNotPublic() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/error");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("/error not found."));
    }
}
