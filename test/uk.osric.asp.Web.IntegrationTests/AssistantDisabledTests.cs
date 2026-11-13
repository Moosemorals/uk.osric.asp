// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

[TestFixture]
public sealed class AssistantDisabledTests : IntegrationTest {

    [Test]
    public async Task AssistantRouteIsNotFoundWhenFeatureIsDisabledAsync() {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/assistant");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task MemoryRouteIsNotFoundWhenFeatureIsDisabledAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/assistant/memory");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EditorDoesNotLinkToDisabledAssistantAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages?edit=/example");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Not.Contain("href=\"/assistant\""));
    }
}
