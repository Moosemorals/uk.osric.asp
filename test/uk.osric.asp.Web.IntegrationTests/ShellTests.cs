// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class ShellTests : IntegrationTest {

    [Test]
    public void StartupMigratesTheThrowawayDatabase() {
        Assert.That(File.Exists(DatabasePath), Is.True);
        Assert.That(DatabasePath, Does.StartWith(Path.GetTempPath()));
    }

    [Test]
    public async Task StoredPageIsRendered() {
        await SavePageAsync("/hello", "Hello, <% ctx.Write(\"world\"); %>!");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/hello");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task UnknownPageReturnsNotFound() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/missing.cs");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("/missing.cs not found."));
    }

    [Test]
    public async Task NonCanonicalPathRedirectsToTheCanonicalPage() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/Hello");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PermanentRedirect));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/hello"));
    }

    [Test]
    public async Task StaticFileIsServedWithoutReachingThePageRoute() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/site.css");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
    }

    [Test]
    public async Task PostPageReceivesTheForm() {
        await SavePageAsync("/echo", "<% ctx.Write(ctx.Request.Form[\"name\"]); %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "/echo", new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "Osric" }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Osric"));
    }

    [Test]
    public async Task PageRedirectIsSentAsFound() {
        await SavePageAsync("/moved", "<% ctx.Redirect(\"/hello\"); %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/moved");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/hello"));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task UnsupportedMethodIsRejectedByRouting() {
        await SavePageAsync("/hello", "Hello.");
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Put, "/hello");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }

    [Test]
    public async Task CompileErrorReturnsDiagnostics() {
        await SavePageAsync("/broken", "<% int value = ; %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/broken");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await response.Content.ReadAsStringAsync(),
            Does.Contain("Handler failed to compile.")
                .And.Contain("line 1, column 16: error CS1525"));
    }

    [Test]
    public async Task UnterminatedCodeBlockIsReportedAsACompileError() {
        await SavePageAsync("/unterminated", "<% if (true) {");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/unterminated");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(body, Does.Contain("Handler failed to compile.").And.Contain("unterminated code block"));
        Assert.That(body, Does.Not.Contain("An unexpected error occurred."),
            "a malformed template is the author's error, not a server fault");
    }
}
