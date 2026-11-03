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
        Assert.That(stylesheetBody, Is.EqualTo(PageAssetSeeder.StylesheetBody));
        Assert.That(stylesheet.Headers.ETag, Is.Not.Null);

        using HttpResponseMessage icon = await client.GetAsync("/rocket.svg");
        Assert.That(icon.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(icon.Content.Headers.ContentType?.ToString(), Is.EqualTo("image/svg+xml"));
        Assert.That(icon.Headers.CacheControl?.NoCache, Is.True);
        Assert.That(await icon.Content.ReadAsStringAsync(), Is.EqualTo(PageAssetSeeder.IconBody));

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
}
