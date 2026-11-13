// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class RestoreEndpointTests : IntegrationTest {

    private const string Broken = "<% this is not valid C# %>";

    [Test]
    public async Task AnAnonymousVisitorIsSentToSignInAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/admin/restore");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.PathAndQuery, Does.StartWith("/login?returnUrl="));
        });
    }

    [Test]
    public async Task APostWithoutAntiforgeryWritesNothingAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);

        HttpResponseMessage response = await client.PostAsync("/admin/restore", new FormUrlEncodedContent([]));
        PageTemplate stored = (await ReadPageAsync("/pages"))!;

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(stored.Template, Is.EqualTo(Broken), "the refused POST restored nothing");
        });
    }

    [Test]
    public async Task TheRestorePageWorksWhenTheSeedsItRecoversAreBrokenAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        await PostEditorAsync(client, "site.css", "* { display: none; }");
        await PostEditorAsync(client, "admin.css", "* { display: none; }");
        await SavePageAsync("/pages", Broken);
        await SavePageAsync("/_layout", Broken);

        HttpResponseMessage preview = await client.GetAsync("/admin/restore");
        string previewBody = await preview.Content.ReadAsStringAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/admin/restore");
        HttpResponseMessage restored = await client.PostAsync(
            "/admin/restore", Form(("__RequestVerificationToken", token)));
        string restoredBody = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(previewBody, Does.Contain("<form method=\"post\" action=\"/admin/restore\""));
            Assert.That(previewBody, Does.Not.Contain("<link"), "the recovery page loads no seeded asset");
            Assert.That(restoredBody, Does.Contain("/site.css"));
            Assert.That(restoredBody, Does.Contain("/_layout"));
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the editor's page serves again");
        });
    }

    [Test]
    public async Task OnePostBringsBackThePageTheEditorNeedsAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/pages", Broken);

        string token = await GetAntiforgeryTokenAsync(client, "/admin/restore");
        HttpResponseMessage restored = await client.PostAsync(
            "/admin/restore", Form(("__RequestVerificationToken", token)));
        string restoredBody = await restored.Content.ReadAsStringAsync();
        HttpResponseMessage pages = await client.GetAsync("/pages");
        string body = await pages.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(restoredBody, Does.Contain("/pages"));
            Assert.That(restoredBody, Does.Contain("1 restored"));
            Assert.That(pages.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<h1>Pages</h1>"));
        });
    }
}
