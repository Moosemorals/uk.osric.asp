// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class StatusEndpointTests {

    [Test]
    public async Task TheDocumentIsTheBuildersOwn() {
        string body = await RenderAsync("");

        string expectedStart = PageDocument.Head("Status", ["/status.css"])
            + PageDocument.Banner(SiteNavigation.Nav(authenticated: false, assistantEnabled: false, "/status", string.Empty))
            + PageDocument.OpenMain();
        string expectedEnd = PageDocument.CloseMain() + PageDocument.End();

        Assert.That(body, Does.StartWith(expectedStart), "the head, menu and landmark come from the document tools");
        Assert.That(body, Does.EndWith(expectedEnd));
    }

    [Test]
    public async Task ThePageSaysWhatItIs() {
        string body = await RenderAsync("");

        Assert.That(body, Does.Contain("<h1>Status</h1>"));
        Assert.That(body, Does.Contain("<nav class=\"status-nav\" aria-label=\"Time window\">"));
        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">1h</span>"));
        Assert.That(body, Does.Not.Contain("<svg"));
    }

    [Test]
    public async Task TheNavLinksEveryWindowButTheCurrentOne() {
        string body = await RenderAsync("duration=7d");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">7d</span>"));
        Assert.That(body, Does.Contain("href=\"/status?duration=1h\""));
        Assert.That(body, Does.Contain("href=\"/status?duration=30d\""));
        Assert.That(body, Does.Not.Contain("href=\"/status?duration=7d\""));
    }

    [Test]
    public async Task AnUnknownDurationIsTheDefaultWindow() {
        string body = await RenderAsync("duration=2w");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">1h</span>"));
    }

    private static async Task<string> RenderAsync(string query) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/status");
        context.Request.QueryString = new QueryString(query.Length == 0 ? string.Empty : "?" + query);

        await StatusEndpoint.HandleAsync(context, TimeProvider.System, CancellationToken.None);

        return await TestHttp.ReadBodyAsync(context);
    }
}
