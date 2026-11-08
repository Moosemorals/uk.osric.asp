// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed partial class ArchiveEndpointTests : IntegrationTest {

    private const string Source = "<% ctx.Write(\"round trip\"); %>";

    [Test]
    public async Task ImportAndExportRequireSignIn() {
        using HttpClient client = CreateClient();

        Assert.That((await client.GetAsync("/admin/export")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That((await client.GetAsync("/admin/import")).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That((await client.PostAsync("/admin/export", Form())).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That((await client.PostAsync("/admin/import", Form())).StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task ImportAndExportRequireAntiforgery() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage export = await client.PostAsync("/admin/export", Form(("include", "/")));
        HttpResponseMessage import = await client.PostAsync("/admin/import", Form(("archive", "x")));

        Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await export.Content.ReadAsStringAsync());
        Assert.That(import.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await import.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task ExportThenImportRoundTripsThroughTheRealHost() {
        using HttpClient client = await CreateSignedInClientAsync();
        HttpResponseMessage saved = await PostEditorAsync(client, "round-trip", Source);
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther), "the page under test must save");
        await SetStateAsync("/round-trip", "visits", "7");

        byte[] archive = await ExportAsync(client, "/round-trip");
        Assert.That(archive, Is.Not.Empty);
        Assert.That(await ReadPageAsync("/round-trip"), Is.Not.Null);

        await WithServicesAsync(async services => {
            await services.GetRequiredService<IPageStore>().DeletePageAsync(PagePath.Normalize("/round-trip"));
            return true;
        });

        await ImportAsync(client, archive);

        Assert.That(await ReadPageAsync("/round-trip"), Is.Not.Null, "the imported page is stored");
        HttpResponseMessage page = await client.GetAsync("/round-trip");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain("round trip"));

        IReadOnlyList<PageStateValue> state = await WithServicesAsync(services =>
            services.GetRequiredService<IPageStateStore>().GetValuesAsync(PagePath.Normalize("/round-trip")));
        Assert.That(state, Is.EqualTo(new List<PageStateValue> { new("visits", "7") }));
    }

    [Test]
    public async Task AnImportedReservedPathIsRefused() {
        using HttpClient client = await CreateSignedInClientAsync();
        byte[] archive = PageArchiveWriter.Write([
            new PageArchiveEntry(PagePath.Normalize("/admin"), "reserved", []),
        ]);

        string token = await GetAntiforgeryTokenAsync(client, "/admin/import");
        HttpResponseMessage response = await client.PostAsync("/admin/import", ArchiveForm(archive, token));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("reserved"));
        Assert.That(await ReadPageAsync("/admin"), Is.Null);
    }

    [Test]
    public async Task AdminPathsAreNotPageKeys() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SavePageAsync("/admin/impersonation", "not reachable");

        Assert.That((await client.GetAsync("/admin/impersonation")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        string token = await GetAntiforgeryTokenAsync(client, "/edit");
        HttpResponseMessage created = await client.PostAsync("/edit?cmd=new", Form(
            ("path", "/admin/elsewhere"),
            ("__RequestVerificationToken", token)));

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<byte[]> ExportAsync(HttpClient client, string path) {
        string token = await GetAntiforgeryTokenAsync(client, "/admin/export");
        HttpResponseMessage response = await client.PostAsync("/admin/export", Form(
            ("include", path),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/vnd.sqlite3"));

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task ImportAsync(HttpClient client, byte[] archive) {
        string token = await GetAntiforgeryTokenAsync(client, "/admin/import");
        HttpResponseMessage upload = await client.PostAsync("/admin/import", ArchiveForm(archive, token));

        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        string review = upload.Headers.Location?.ToString() ?? throw new AssertionException("no review redirect");

        string include = await PageKeysAsync(client, review);
        string reviewToken = await GetAntiforgeryTokenAsync(client, review);
        HttpResponseMessage approved = await client.PostAsync(review, Form(
            ("include", include),
            ("__RequestVerificationToken", reviewToken)));

        Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        string progress = approved.Headers.Location?.ToString() ?? throw new AssertionException("no progress redirect");

        for (int attempt = 0; attempt < 100; attempt++) {
            string body = await (await client.GetAsync(progress)).Content.ReadAsStringAsync();
            if (!body.Contains("http-equiv=\"refresh\"", StringComparison.Ordinal)) {
                Assert.That(body, Does.Contain("saved"));
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("the import did not finish");
    }

    // The review's first checkbox carries the page key the approval has to tick.
    private static async Task<string> PageKeysAsync(HttpClient client, string review) {
        string body = await (await client.GetAsync(review)).Content.ReadAsStringAsync();
        Match match = IncludeInput().Match(body);

        Assert.That(match.Success, Is.True, "the review offers no pages");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task SetStateAsync(string path, string key, string value) {
        await WithServicesAsync(async services => {
            await services.GetRequiredService<IPageStateStore>()
                .SetValueAsync(PagePath.Normalize(path), key, value);
            return true;
        });
    }

    private static MultipartFormDataContent ArchiveForm(byte[] archive, string token) {
        ByteArrayContent file = new(archive);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.sqlite3");

        return new MultipartFormDataContent {
            { new StringContent(token), "__RequestVerificationToken" },
            { file, "archive", "pages.sqlite" },
        };
    }

    [GeneratedRegex("name=\"include\" value=\"([^\"]+)\"")]
    private static partial Regex IncludeInput();
}

/// <summary>The upload cap is a per-deployment setting, so it needs a host of its own.</summary>
[TestFixture]
public sealed class ArchiveUploadLimitTests : IntegrationTest {

    internal override AspApplication CreateApplication() {
        return new AspApplication(archiveUploadBytes: 1024);
    }

    [Test]
    public async Task AnUploadOverTheConfiguredCapAnswers413() {
        using HttpClient client = await CreateSignedInClientAsync();
        string token = await GetAntiforgeryTokenAsync(client, "/admin/import");

        ByteArrayContent file = new(new byte[4096]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.sqlite3");
        using MultipartFormDataContent form = new() {
            { new StringContent(token), "__RequestVerificationToken" },
            { file, "archive", "pages.sqlite" },
        };

        HttpResponseMessage response = await client.PostAsync("/admin/import", form);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }
}
