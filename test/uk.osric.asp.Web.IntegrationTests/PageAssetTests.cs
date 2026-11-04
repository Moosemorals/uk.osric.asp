// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
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

        using HttpResponseMessage layoutStylesheet = await client.GetAsync("/layout.css");
        Assert.That(layoutStylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(layoutStylesheet.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(layoutStylesheet.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(await layoutStylesheet.Content.ReadAsStringAsync(), Is.EqualTo(RenderedSeed("layout.css")));

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
    public async Task AdminRestoreRecreatesADeletedSeedAndKeepsItsHistory() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/editor.css", "/* local edit */");
        StoredPage edited = (await ReadPageAsync("/editor.css"))!;

        string deleteToken = await GetAntiforgeryTokenAsync(client, "/edit/editor.css?cmd=delete");
        using HttpResponseMessage deleted = await client.PostAsync("/edit/editor.css?cmd=delete", Form(
            ("__RequestVerificationToken", deleteToken)));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(await ReadPageAsync("/editor.css"), Is.Null);

        using HttpResponseMessage editor = await client.GetAsync("/edit/editor.css");
        string editorBody = await editor.Content.ReadAsStringAsync();
        Assert.That(editorBody, Does.Contain("Restore bundled version"));
        Assert.That(editorBody, Does.Contain("/admin/restore?path=%2Feditor.css"));

        const string confirmationPath = "/admin/restore?path=%2Feditor.css";
        string seedSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", "editor.css"));
        string restoreToken = await GetAntiforgeryTokenAsync(client, confirmationPath);
        using HttpResponseMessage confirmation = await client.GetAsync(confirmationPath);
        string confirmationBody = await confirmation.Content.ReadAsStringAsync();
        Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(confirmationBody, Does.Contain("will replace the current page content"));
        Assert.That(confirmationBody, Does.Contain(WebUtility.HtmlEncode(seedSource)));
        Assert.That(confirmationBody, Does.Contain("name=\"__RequestVerificationToken\""));

        using HttpResponseMessage restore = await client.PostAsync("/admin/restore", Form(
            ("__RequestVerificationToken", restoreToken),
            ("path", "/editor.css")));
        StoredPage restored = (await ReadPageAsync("/editor.css"))!;
        Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(restore.Headers.Location?.ToString(), Is.EqualTo("/edit/editor.css"));
        Assert.That(restore.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(restored.Content, Is.EqualTo(seedSource));
        Assert.That(restored.Hash, Is.Not.EqualTo(edited.Hash));

        using HttpResponseMessage history = await client.GetAsync("/edit/editor.css?cmd=history");
        Assert.That(await history.Content.ReadAsStringAsync(), Does.Contain(edited.Hash));

        using HttpResponseMessage restoredEditor = await client.GetAsync("/edit/editor.css");
        Assert.That(await restoredEditor.Content.ReadAsStringAsync(), Does.Contain("Restored the bundled version."));
    }

    [Test]
    public async Task AdminRestoreIsAuthenticatedAndRequiresAntiforgery() {
        using HttpClient anonymous = CreateClient();
        using HttpResponseMessage loginRedirect = await anonymous.GetAsync("/admin/restore?path=%2Feditor.css");
        Assert.That(loginRedirect.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(loginRedirect.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));

        using HttpClient client = await CreateSignedInClientAsync();
        using HttpResponseMessage missingToken = await client.PostAsync("/admin/restore", Form(("path", "/editor.css")));

        Assert.That(missingToken.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(missingToken.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await ReadPageAsync("/editor.css"), Is.Not.Null);
    }

    [Test]
    public async Task AdminRestoreRejectsPagesWithoutABundledVersion() {
        using HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.GetAsync("/admin/restore?path=%2Fnot-a-seed");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    private static string RenderedSeed(string name) {
        string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", name));
        string[] parts = template.Split("<%", StringSplitOptions.None);
        return parts[0] + parts[1][(parts[1].IndexOf("%>", StringComparison.Ordinal) + 2)..];
    }
}
