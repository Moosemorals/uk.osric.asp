// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>The seeded layout is a page, so editing it changes every page on its next request.</summary>
[TestFixture]
public sealed class SiteLayoutEditingTests : IntegrationTest {

    private const string Fragment = """
        <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "A fragment"; %><h1>Fragment</h1>
        """;

    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    [Test]
    public async Task SavingTheLayoutChangesEveryPageOnItsNextRequest() {
        await SavePageAsync("/will-change", Fragment);
        using HttpClient client = await CreateSignedInClientAsync();

        Assert.That(await (await client.GetAsync("/will-change")).Content.ReadAsStringAsync(),
            Does.Not.Contain("Chrome from the editor"));

        HttpResponseMessage saved = await PostEditorAsync(client, "_layout", """
            <%! using Site.Document; %><% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               Document.Head(ctx, layout.Title, layout.Stylesheets, layout.Head, ctx.AssetUrl); %>
            <p>Chrome from the editor</p><% Document.OpenMain(ctx); %><%== layout.Body %><% Document.CloseMain(ctx); Document.End(ctx); %>
            """);
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string body = await (await client.GetAsync("/will-change")).Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Chrome from the editor"));
    }

}
