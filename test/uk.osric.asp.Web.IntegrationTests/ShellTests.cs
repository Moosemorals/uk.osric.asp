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
    public async Task CompileErrorReturnsDiagnostics() {
        await SavePageAsync("/broken", "<% int value = ; %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/broken");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await response.Content.ReadAsStringAsync(),
            Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }
}
