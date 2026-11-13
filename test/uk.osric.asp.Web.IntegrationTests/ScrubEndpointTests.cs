// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>Scrubbing through the real host: the shell's route, its typed key and its refusals.</summary>
[TestFixture]
public sealed class ScrubEndpointTests : IntegrationTest {

    [Test]
    public async Task TheConfirmationAsksForTheKeyAndWarnsWhatScrubbingCannotDoAsync() {
        await DeletedPageAsync("/secrets");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/admin/scrub/secrets");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Type /secrets to confirm"));
            Assert.That(body, Does.Contain("cannot be undone"));
            Assert.That(body, Does.Contain("action=\"/admin/scrub/secrets\""), "the form posts to the route that answered");
        });
    }

    [Test]
    public async Task TheTypedKeyRemovesThePageAndReturnsToTheEditorPageAsync() {
        await DeletedPageAsync("/secrets");
        using HttpClient client = await CreateSignedInClientAsync();

        string confirmation = await GetAntiforgeryTokenAsync(client, "/admin/scrub/secrets");
        HttpResponseMessage scrub = await PostConfirmationAsync(client, "/admin/scrub/secrets", confirmation, "/secrets");

        Assert.Multiple(() => {
            Assert.That(scrub.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
            Assert.That(scrub.Headers.Location?.ToString(), Is.EqualTo("/pages"));
        });
    }

    [Test]
    public async Task AKeyThatIsNotTypedExactlyRemovesNothingAsync() {
        await DeletedPageAsync("/secrets");
        using HttpClient client = await CreateSignedInClientAsync();

        string confirmation = await GetAntiforgeryTokenAsync(client, "/admin/scrub/secrets");
        HttpResponseMessage scrub = await PostConfirmationAsync(client, "/admin/scrub/secrets", confirmation, "secrets");
        bool tombstone = await HeadExistsAsync("/secrets");

        Assert.Multiple(() => {
            Assert.That(scrub.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(tombstone, Is.True, "the tombstone is still there");
        });
    }

    [Test]
    public async Task ALivePageIsRefusedAndNamesTheDeleteThatComesFirstAsync() {
        await SavePageAsync("/live", "still served");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/admin/scrub/live");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(body, Does.Contain("Delete the page before scrubbing its history"));
        });
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        await DeletedPageAsync("/secrets");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/scrub/secrets");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        });
    }

    // A tombstone keeps its head but has no live page, so GetHeadAsync is what says it is still there.
    private Task<bool> HeadExistsAsync(string path) => WithServicesAsync(async services =>
        await services.GetRequiredService<IPageStore>().GetHeadAsync(PagePath.Normalize(path)) is not null);

    private async Task DeletedPageAsync(string path) {
        await SavePageAsync(path, "<p>secret</p>");
        await WithServicesAsync(async services => {
            IPageStore store = services.GetRequiredService<IPageStore>();
            return await store.DeletePageAsync(PagePath.Normalize(path));
        });
    }

    private static Task<HttpResponseMessage> PostConfirmationAsync(
        HttpClient client, string url, string token, string confirmation) =>
        client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = token,
            ["confirmation"] = confirmation,
        }));
}
