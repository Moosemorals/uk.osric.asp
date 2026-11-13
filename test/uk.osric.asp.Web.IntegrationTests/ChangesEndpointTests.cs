// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Web.Admin;

[TestFixture]
public sealed class ChangesEndpointTests : IntegrationTest {

    private readonly string _changelist =
        Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changelist.{Guid.NewGuid():N}.txt");

    internal override AspApplication CreateApplication() {
        File.WriteAllText(_changelist, "0123456789abcdef0123456789abcdef01234567 The first commit\n");
        return new AspApplication(changelist: _changelist);
    }

    [OneTimeTearDown]
    public void RemoveChangelist() => File.Delete(_changelist);

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/changes");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        });
    }

    [Test]
    public async Task TheOwnerSeesEveryCommitLinkedToGitHubAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/admin/changes");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(body, Does.Contain(
                $"{ChangesEndpoint.RepositoryUrl}/commit/0123456789abcdef0123456789abcdef01234567"));
            Assert.That(body, Does.Contain("The first commit"));
        });
    }
}
