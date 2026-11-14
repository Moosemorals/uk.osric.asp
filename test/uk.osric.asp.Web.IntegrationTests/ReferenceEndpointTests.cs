// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Web.Documentation;

/// <summary>The reference through the composed host: public, and reading the build it serves.</summary>
[TestFixture]
public sealed class ReferenceEndpointTests : IntegrationTest {

    [Test]
    public async Task AnAnonymousVisitorReadsTheReferenceAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/reference");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the key is under /admin, the page is public");
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(body, Does.Contain("The page contract"));
            Assert.That(body, Does.Contain("href=\"/admin/reference/sdk\""));
        });
    }

    [Test]
    public async Task TheContractPageNamesItsTypesAndMembersAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/reference/sdk");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("id=\"t-IContext\""));
            Assert.That(body, Does.Contain("id=\"m-IPageStorage.TryGetValueAsync-g1-string\""));
            Assert.That(body, Does.Contain(ReferenceDisplay.LearnBase));
        });
    }

    [Test]
    public async Task AnUnknownSlugIsANotFoundAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/reference/nope");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
