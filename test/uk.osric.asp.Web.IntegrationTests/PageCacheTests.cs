// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

/// <summary>Proves, through the real host, that a page is compiled once rather than per request.</summary>
[TestFixture]
public sealed class PageCacheTests : IntegrationTest {

    [Test]
    public async Task AStoredPageIsCompiledOnceAndThenServedFromItsImage() {
        await SavePageAsync("/cached", "Hello.");
        using HttpClient client = CreateClient();
        int compiles = CompileCount;

        string first = await (await client.GetAsync("/cached")).Content.ReadAsStringAsync();
        string second = await (await client.GetAsync("/cached")).Content.ReadAsStringAsync();

        Assert.That(first, Is.EqualTo("Hello."));
        Assert.That(second, Is.EqualTo("Hello."));
        Assert.That(CompileCount - compiles, Is.EqualTo(1), "the second request must not compile the page again");
    }

    [Test]
    public async Task SavingCompilesOnceAndViewingDoesNotCompileAgain() {
        using HttpClient client = await CreateSignedInClientAsync();
        int compiles = CompileCount;

        await PostEditorAsync(client, "saved", "Saved.");
        string body = await (await client.GetAsync("/saved")).Content.ReadAsStringAsync();

        Assert.That(body, Is.EqualTo("Saved."));
        Assert.That(CompileCount - compiles, Is.EqualTo(1), "the save compiled the page; the view must not");
    }

    [Test]
    public async Task ABrokenSaveShowsDiagnosticsWithoutCompilingAgain() {
        using HttpClient client = await CreateSignedInClientAsync();
        int compiles = CompileCount;

        await PostEditorAsync(client, "broken", "<% int value = ; %>");

        string editor = await (await client.GetAsync("/edit/broken")).Content.ReadAsStringAsync();
        Assert.That(editor, Does.Contain("Saved with errors."));
        Assert.That(editor, Does.Contain("error CS"));

        HttpResponseMessage page = await client.GetAsync("/broken");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await page.Content.ReadAsStringAsync(), Does.Contain("error CS"));
        Assert.That(CompileCount - compiles, Is.EqualTo(1), "the stored diagnostics must be served, not recompiled");
    }

    [Test]
    public async Task AnEditIsVisibleOnTheNextRequest() {
        using HttpClient client = await CreateSignedInClientAsync();
        await PostEditorAsync(client, "live", "first");
        await client.GetAsync("/live");

        await PostEditorAsync(client, "live", "second");

        Assert.That(await (await client.GetAsync("/live")).Content.ReadAsStringAsync(), Is.EqualTo("second"));
    }
}
