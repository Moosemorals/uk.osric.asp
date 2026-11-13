// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Text.RegularExpressions;

/// <summary>The seeded `/pages` over a real page store: read, edit, save and refuse.</summary>
[TestFixture]
public sealed partial class PageEditorTests : IntegrationTest {

    [Test]
    public async Task TheListingIsForTheOwnerOnlyAsync() {
        using HttpClient anonymous = CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.ToString(), Does.StartWith("/login?returnUrl="));
        });
    }

    [Test]
    public async Task ASaveThroughThePageIsServedOnTheNextRequestAsync() {
        await SavePageAsync("/editor-round-trip", "<h1>before</h1>");
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage answer = await SaveAsync(client, "/editor-round-trip", "<h1>after</h1>");
        string answerBody = await answer.Content.ReadAsStringAsync();
        using HttpResponseMessage served = await client.GetAsync("/editor-round-trip");
        string servedBody = await served.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answerBody, Does.Contain("Saved as revision"), "the page answers the save");
            Assert.That(servedBody, Does.Contain("<h1>after</h1>"), "and the next request serves it");
        });
    }

    [Test]
    public async Task AStaleSaveIsAConflictThatKeepsTheDraftAsync() {
        await SavePageAsync("/editor-stale", "<h1>stored</h1>");
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/pages?edit=/editor-stale");

        HttpResponseMessage response = await client.PostAsync("/pages?edit=/editor-stale", Form(
            ("__RequestVerificationToken", token),
            ("template", "<h1>my draft</h1>"),
            ("expected", new string('a', 64)),
            ("cmd", "save")));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(body, Does.Contain("my draft"), "the author's text comes back");
            Assert.That(body, Does.Contain("stored"), "and the source that was kept");
        });
    }

    [Test]
    public async Task AShellFileIsNotEditableThroughThePageAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages?edit=/login");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("shell file"));
        });
    }

    [Test]
    public async Task TheSeededEditorIsWhatTheHostServesAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/pages");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<h1>Pages</h1>"));
            Assert.That(body, Does.Contain("href=\"/pages?edit="), "every page opens in the editor");
            Assert.That(body, Does.Contain("action=\"/pages?cmd=new\""), "and the page list creates one");
        });
    }

    // The form carries the head hash it was rendered from, so a test has to read it the way a browser does.
    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string key, string template) {
        string path = "/pages?edit=" + Uri.EscapeDataString(key);
        string editor = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
        Match expected = Expected().Match(editor);
        Assert.That(expected.Success, Is.True, "the editor rendered no head hash");

        string token = await GetAntiforgeryTokenAsync(client, path);

        return await client.PostAsync(path, Form(
            ("__RequestVerificationToken", token),
            ("template", template),
            ("expected", expected.Groups[1].Value),
            ("cmd", "save")));
    }

    [GeneratedRegex("name=\"expected\" value=\"([0-9a-f]{64})\"")]
    private static partial Regex Expected();
}
