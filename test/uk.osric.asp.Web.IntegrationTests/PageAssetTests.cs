// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Text;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetTests : IntegrationTest {

    [Test]
    public async Task BuiltInAssetsAreSeededAndRevalidatedAfterAnEditorSave() {
        using HttpClient client = CreateClient();
        using HttpResponseMessage stylesheet = await client.GetAsync("/site.css");
        string stylesheetBody = await stylesheet.Content.ReadAsStringAsync();

        Assert.That(stylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(stylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(stylesheet.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(stylesheet.Content.Headers.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(stylesheetBody)));
        Assert.That(stylesheetBody, Is.EqualTo(RenderedSeed("site.css")));
        Assert.That(stylesheet.Headers.ETag, Is.Not.Null);

        using HttpResponseMessage icon = await client.GetAsync("/rocket.svg");
        Assert.That(icon.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(icon.Content.Headers.ContentType?.ToString(), Is.EqualTo("image/svg+xml"));
        Assert.That(icon.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(await icon.Content.ReadAsStringAsync(), Is.EqualTo(RenderedSeed("rocket.svg")));

        using HttpResponseMessage statusStylesheet = await client.GetAsync("/status.css");
        Assert.That(statusStylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(statusStylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(statusStylesheet.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(await statusStylesheet.Content.ReadAsStringAsync(), Is.EqualTo(RenderedSeed("status.css")));

        using HttpResponseMessage editorStylesheet = await client.GetAsync("/editor.css");
        Assert.That(editorStylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(editorStylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(editorStylesheet.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(await editorStylesheet.Content.ReadAsStringAsync(), Is.EqualTo(RenderedSeed("editor.css")));

        using HttpResponseMessage editorScript = await client.GetAsync("/editor.js");
        string editorScriptBody = await editorScript.Content.ReadAsStringAsync();
        Assert.That(editorScript.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(editorScript.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(editorScript.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(editorScriptBody, Is.EqualTo(RenderedSeed("editor.js")));
        Assert.That(editorScriptBody, Does.Not.Contain("<%"), "template code in the script would be a page error");

        using HttpResponseMessage assistantStylesheet = await client.GetAsync("/assistant.css");
        Assert.That(assistantStylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(assistantStylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(await assistantStylesheet.Content.ReadAsStringAsync(), Is.EqualTo(RenderedSeed("assistant.css")));

        using HttpResponseMessage assistantScript = await client.GetAsync("/assistant.js");
        string assistantScriptBody = await assistantScript.Content.ReadAsStringAsync();
        Assert.That(assistantScript.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(assistantScript.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(assistantScript.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(assistantScriptBody, Is.EqualTo(RenderedSeed("assistant.js")));
        Assert.That(assistantScriptBody, Does.Not.Contain("<%"));

        using HttpResponseMessage login = await client.GetAsync("/login");
        string loginBody = await login.Content.ReadAsStringAsync();
        Assert.That(loginBody, Does.Contain("<link rel=\"stylesheet\" href=\"/site.css\">"));
        Assert.That(loginBody, Does.Contain("<link rel=\"icon\" href=\"/rocket.svg\" type=\"image/svg+xml\">"));

        using HttpResponseMessage legacyIcon = await client.GetAsync("/favicon.ico");
        Assert.That(legacyIcon.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        using HttpRequestMessage getUnchanged = new(HttpMethod.Get, "/site.css");
        getUnchanged.Headers.IfNoneMatch.Add(stylesheet.Headers.ETag!);
        using HttpResponseMessage notModified = await client.SendAsync(getUnchanged);
        Assert.That(notModified.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(await notModified.Content.ReadAsStringAsync(), Is.Empty);

        using HttpRequestMessage headUnchanged = new(HttpMethod.Head, "/site.css");
        headUnchanged.Headers.IfNoneMatch.Add(stylesheet.Headers.ETag!);
        using HttpResponseMessage headNotModified = await client.SendAsync(headUnchanged);
        Assert.That(headNotModified.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(await headNotModified.Content.ReadAsStringAsync(), Is.Empty);

        using HttpClient editor = await CreateSignedInClientAsync();
        const string updatedBody = "body { color: red; }\n";
        string updatedTemplate = updatedBody
            + "<% ctx.SetContentType(\"text/css; charset=utf-8\"); ctx.SetHeader(\"Cache-Control\", \"no-cache\"); %>";
        using HttpResponseMessage save = await PostEditorAsync(editor, "site.css", updatedTemplate);
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));

        using HttpResponseMessage changed = await client.GetAsync("/site.css");
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await changed.Content.ReadAsStringAsync(), Is.EqualTo(updatedBody));
        Assert.That(changed.Content.Headers.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(updatedBody)));
        Assert.That(changed.Headers.ETag, Is.Not.EqualTo(stylesheet.Headers.ETag));
    }

    [Test]
    public async Task ThePageListRestoresADeletedSeedAndKeepsItsHistory() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/editor.css", "/* local edit */");
        PageTemplate edited = (await ReadPageAsync("/editor.css"))!;

        string deleteToken = await GetAntiforgeryTokenAsync(client, "/edit/editor.css?cmd=delete");
        using HttpResponseMessage deleted = await client.PostAsync("/edit/editor.css?cmd=delete", Form(
            ("__RequestVerificationToken", deleteToken)));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await ReadPageAsync("/editor.css"), Is.Null);

        using HttpResponseMessage listing = await client.GetAsync("/edit");
        string listingBody = await listing.Content.ReadAsStringAsync();
        Assert.That(listingBody, Does.Contain("method=\"post\" action=\"/edit/editor.css?cmd=restore\""));
        Assert.That(listingBody, Does.Contain("aria-label=\"Restore /editor.css from its seed\">Restore from seed</button>"));

        using HttpResponseMessage editor = await client.GetAsync("/edit/editor.css");
        Assert.That(await editor.Content.ReadAsStringAsync(), Does.Not.Contain("Restore bundled version"));

        string restoreToken = await GetAntiforgeryTokenAsync(client, "/edit");
        using HttpResponseMessage restore = await client.PostAsync("/edit/editor.css?cmd=restore", Form(
            ("__RequestVerificationToken", restoreToken)));
        PageTemplate restored = (await ReadPageAsync("/editor.css"))!;
        Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(restore.Headers.Location?.ToString(), Is.EqualTo("/edit"));
        Assert.That(restore.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(restored.Template, Is.EqualTo(ReadSeed("editor.css")));
        Assert.That(restored.Hash, Is.Not.EqualTo(edited.Hash));

        using HttpResponseMessage history = await client.GetAsync("/edit/editor.css?cmd=history");
        Assert.That(await history.Content.ReadAsStringAsync(), Does.Contain(edited.Hash));

        using HttpResponseMessage after = await client.GetAsync("/edit");
        Assert.That(await after.Content.ReadAsStringAsync(), Does.Contain("Restored /editor.css from its seed."));
    }

    [Test]
    public async Task PageListRestoreIsAuthenticatedAndRequiresAntiforgery() {
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage loginRedirect = await anonymous.GetAsync("/edit");
        Assert.That(loginRedirect.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(loginRedirect.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));

        using HttpClient client = await CreateSignedInClientAsync();
        using HttpResponseMessage missingToken = await client.PostAsync("/edit/editor.css?cmd=restore", Form());

        Assert.That(missingToken.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(missingToken.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await ReadPageAsync("/editor.css"), Is.Not.Null);
    }

    [Test]
    public async Task RestoreRejectsPagesWithoutABundledVersion() {
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/edit");

        using HttpResponseMessage response = await client.PostAsync("/edit/not-a-seed?cmd=restore", Form(
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static string ReadSeed(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", name));

    private static string RenderedSeed(string name) {
        string template = ReadSeed(name);
        int start = template.IndexOf("<%", StringComparison.Ordinal);
        if (start < 0) {
            return template;
        }

        int end = template.IndexOf("%>", start, StringComparison.Ordinal);
        return template[..start] + template[(end + 2)..];
    }
}
