// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary><c>/edit</c> is an ordinary key now, answered by whatever page is stored there.</summary>
[TestFixture]
public sealed class RetiredEditorPathTests : IntegrationTest {

    [TestCase("/edit")]
    [TestCase("/edit/anything")]
    [TestCase("/edit/a/b")]
    public async Task TheRetiredEditorPathsAreOrdinaryMissingPagesAsync(string path) {
        // Another test in this fixture stores a page at `/edit`, so the key is emptied first: these
        // assertions are about routing, not about what else has run.
        await WithServicesAsync(async services => await services
            .GetRequiredService<IPageStore>()
            .DeletePageAsync(PagePath.Normalize(path)));
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AnAnonymousPostToTheRetiredPathIsNotFoundRatherThanRefusedAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync("/edit", Form(("template", "hijacked")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "the page catch-all answers it, so the host's antiforgery check never sees a route");
    }

    [Test]
    public async Task StoringAPageAtTheRetiredPathMakesItServeAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage save = await PostEditorAsync(client, "edit", "Stored at the retired key.");
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage response = await client.GetAsync("/edit");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Stored at the retired key."));
    }
}
