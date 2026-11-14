// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page of declarations is checked, listed, and never served.</summary>
[TestFixture]
public sealed class PageLibraryTests : IntegrationTest {

    [Test]
    public async Task ASavedLibraryIsListedAsOneAndItsUrlIsNotFoundAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage saved = await PostEditorAsync(client, "_lib/document",
            "<%! namespace Site { public class Document { } } %>");
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage page = await client.GetAsync("/_lib/document");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a library has no representation to serve");

        string listing = await (await client.GetAsync("/pages")).Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(listing, Does.Contain("/_lib/document"));
            Assert.That(listing, Does.Contain("Library"));
            Assert.That(listing, Does.Not.Contain("aria-label=\"View /_lib/document\""));
        });
    }
}
