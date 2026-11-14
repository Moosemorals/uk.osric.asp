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

        HttpResponseMessage saved = await PostEditorAsync(client, "lib/greeting",
            "<%! namespace Site { public class Greeting { } } %>");
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage page = await client.GetAsync("/lib/greeting");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a library has no representation to serve");

        string listing = await (await client.GetAsync("/pages")).Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(listing, Does.Contain("/lib/greeting"));
            Assert.That(listing, Does.Contain("Library"));
            Assert.That(listing, Does.Not.Contain("aria-label=\"View /lib/greeting\""));
        });
    }

    [Test]
    public async Task APageUsesASavedLibraryThroughTheRealHostAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage library = await PostEditorAsync(client, "lib/greeting",
            "<%! namespace Site { public static class Greeting { public static string Text() => \"from the library\"; } } %>");
        Assert.That(library.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage page = await PostEditorAsync(client, "uses-library",
            "<%! using Site; %><h1><% ctx.Write(Greeting.Text()); %></h1>");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string body = await (await client.GetAsync("/uses-library")).Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("from the library"));
    }
}
