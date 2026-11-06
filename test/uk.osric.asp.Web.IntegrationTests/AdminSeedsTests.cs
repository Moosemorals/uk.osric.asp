// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class AdminSeedsTests : IntegrationTest {

    [Test]
    public async Task ThePageIsBehindTheEditorPolicyAsync() {
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/admin");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
    }

    [Test]
    public async Task ThePageListsSeedsAndRestoresOneAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/assistant.css", "/* local edit */");

        using HttpResponseMessage page = await client.GetAsync("/admin");
        string body = await page.Content.ReadAsStringAsync();
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("/assistant.css"));
        Assert.That(body, Does.Contain("<td>Edited</td>"));
        Assert.That(body, Does.Contain("Restore all seeds"));

        string token = await GetAntiforgeryTokenAsync(client, "/admin");
        using HttpResponseMessage restored = await client.PostAsync("/admin/seeds/restore", Form(
            ("__RequestVerificationToken", token),
            ("path", "/assistant.css")));

        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(restored.Headers.Location?.ToString(), Is.EqualTo("/admin"));
        Assert.That(restored.Headers.CacheControl?.NoStore, Is.True);
        Assert.That((await ReadPageAsync("/assistant.css"))?.Template, Is.EqualTo(ReadSeed("assistant.css")));

        using HttpResponseMessage after = await client.GetAsync("/admin");
        string afterBody = await after.Content.ReadAsStringAsync();
        Assert.That(afterBody, Does.Contain("Restored /assistant.css."));
        Assert.That(afterBody, Does.Contain("<td>Matches seed</td>"));
    }

    [Test]
    public async Task RestoreAllReplacesEveryChangedSeedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/assistant.css", "/* local edit */");
        await SavePageAsync("/editor.css", "/* local edit */");
        string deleteToken = await GetAntiforgeryTokenAsync(client, "/edit/status.css?cmd=delete");
        using HttpResponseMessage deleted = await client.PostAsync("/edit/status.css?cmd=delete", Form(
            ("__RequestVerificationToken", deleteToken)));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        string token = await GetAntiforgeryTokenAsync(client, "/admin");
        using HttpResponseMessage restored = await client.PostAsync("/admin/seeds/restore-all", Form(
            ("__RequestVerificationToken", token)));

        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(restored.Headers.Location?.ToString(), Is.EqualTo("/admin"));
        Assert.That((await ReadPageAsync("/assistant.css"))?.Template, Is.EqualTo(ReadSeed("assistant.css")));
        Assert.That((await ReadPageAsync("/editor.css"))?.Template, Is.EqualTo(ReadSeed("editor.css")));
        Assert.That((await ReadPageAsync("/status.css"))?.Template, Is.EqualTo(ReadSeed("status.css")));

        using HttpResponseMessage after = await client.GetAsync("/admin");
        Assert.That(await after.Content.ReadAsStringAsync(), Does.Contain("Restored 3 seeded pages; 5 already matched."));
    }

    [Test]
    public async Task RestoreRequiresAntiforgeryAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.PostAsync("/admin/seeds/restore", Form(("path", "/assistant.css")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RestoreAllRequiresAntiforgeryAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.PostAsync("/admin/seeds/restore-all", Form());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static string ReadSeed(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", name));
}
