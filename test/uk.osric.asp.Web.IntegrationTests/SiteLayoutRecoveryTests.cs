// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A site whose layout has been deleted still answers its own recovery routes.</summary>
[TestFixture]
public sealed class SiteLayoutRecoveryTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    [Test]
    public async Task DeletingTheLayoutLeavesEveryShellViewAnswering() {
        using HttpClient client = await CreateSignedInClientAsync();

        string token = await GetAntiforgeryTokenAsync(client, "/pages?edit=/_layout&cmd=delete");
        HttpResponseMessage deleted = await client.PostAsync("/pages?edit=/_layout&cmd=delete",
            Form(("__RequestVerificationToken", token)));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await client.GetAsync("/pages")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/status")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/logout")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

}
