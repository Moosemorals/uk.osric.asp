// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using System.Net.Http;

/// <summary>Proves a page that outruns its budget is answered, not waited on, by the real host.</summary>
[TestFixture]
public sealed class RenderBudgetTests : IntegrationTest {

    // Neither Thread.Sleep nor Task.Delay may be used by a page (the API policy refuses them), so a
    // slow page burns CPU against a wall clock. It still ends by itself.
    private const string SlowPage =
        "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1500); while (System.DateTime.UtcNow < until) { } %>";

    internal override AspApplication CreateApplication() {
        return new AspApplication(renderTimeoutSeconds: 1);
    }

    [Test]
    public async Task APageThatOutrunsTheBudgetIsAnsweredWithGatewayTimeoutAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await PostEditorAsync(client, "slow", SlowPage);

        using HttpResponseMessage response = await client.GetAsync("/slow");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("/slow is taking too long."));
    }

    [Test]
    public async Task TheSiteStillServesAfterAPageIsAbandonedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await PostEditorAsync(client, "hello", "<p>Hello.</p>");
        await PostEditorAsync(client, "slow", SlowPage);

        using HttpResponseMessage slow = await client.GetAsync("/slow");
        using HttpResponseMessage hello = await client.GetAsync("/hello");

        Assert.That(slow.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
        Assert.That(hello.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
