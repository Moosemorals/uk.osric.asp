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
            "<%! using Site; %><h1><% ctx.Response.Write(Greeting.Text()); %></h1>");
        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string body = await (await client.GetAsync("/uses-library")).Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("from the library"));
    }

    // What a library edit recompiles, read off the listing: the closure is the compiler's, so the
    // page that names the library carries its key and the page that does not carries nothing.
    [Test]
    public async Task AListingSaysWhichLibrariesAPageNamesAsync() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage library = await PostEditorAsync(client, "lib/greeting",
            "<%! namespace Site { public static class Greeting { public static string Text() => \"hi\"; } } %>");
        Assert.That(library.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await PostEditorAsync(client, "uses-library",
            "<%! using Site; %><h1><% ctx.Response.Write(Greeting.Text()); %></h1>")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await PostEditorAsync(client, "plain-page", "<h1>Plain</h1>")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        HttpResponseMessage listing = await PostEditorAsync(client, "closure-listing", """
            <% foreach (PageEntry entry in System.Linq.Enumerable.Where(
                   (await ctx.Capabilities.GetRequired<IPageCatalog>().ListAsync(new())).Entries,
                   entry => entry.Hash is not null)) { %>
            <p><%= entry.Key %>|<%= string.Join(",", entry.Libraries) %>|</p>
            <% } %>
            """);
        Assert.That(listing.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string body = await (await client.GetAsync("/closure-listing")).Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("/uses-library|/lib/greeting|"));
            Assert.That(body, Does.Contain("/plain-page||"));
        });
    }
}
