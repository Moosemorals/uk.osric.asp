// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class PageStorageTests : IntegrationTest {

    private const string Counter = """
        <% IPageStorage storage = ctx.Capabilities.GetRequired<IPageStorage>();
           PageValue<int> stored = await storage.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await storage.SetAsync("visits", visits); %>
        <p>Visited <%= visits.ToString() %> times.</p>
        """;

    [Test]
    public async Task StateSurvivesRequestsAnEditAndAResaveAsync() {
        await SavePageAsync("/counter", Counter);
        using HttpClient client = CreateClient();

        Assert.That(await client.GetStringAsync("/counter"), Does.Contain("Visited 1 times."));
        Assert.That(await client.GetStringAsync("/counter"), Does.Contain("Visited 2 times."));

        await SavePageAsync("/counter", Counter.Replace("Visited", "Seen", StringComparison.Ordinal));
        Assert.That(await client.GetStringAsync("/counter"), Does.Contain("Seen 3 times."));
    }

    [Test]
    public async Task AHeadRequestReadsStateButDoesNotWriteItAsync() {
        await SavePageAsync("/counter", Counter);
        using HttpClient client = CreateClient();
        await client.GetStringAsync("/counter");

        using HttpRequestMessage head = new(HttpMethod.Head, "/counter");
        HttpResponseMessage response = await client.SendAsync(head);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await client.GetStringAsync("/counter"), Does.Contain("Visited 2 times."));
    }

    [Test]
    public async Task DeletingAPageClearsItsStateAsync() {
        await SavePageAsync("/counter", Counter);
        using HttpClient client = CreateClient();
        await client.GetStringAsync("/counter");
        await client.GetStringAsync("/counter");

        await WithServicesAsync(async services => {
            await services.GetRequiredService<IPageStore>().DeletePageAsync(PagePath.Normalize("/counter"));
            return true;
        });

        int rows = await WithServicesAsync(services => services.GetRequiredService<StorageDbContext>()
            .Set<PageStateEntry>()
            .Where(row => row.PagePath == "/counter")
            .CountAsync());

        Assert.That(rows, Is.Zero);
    }

    [Test]
    public async Task AnotherPageCannotSeeTheKeysAsync() {
        await SavePageAsync("/counter", Counter);
        await SavePageAsync("/other", """
            <p>keys: <%= (await ctx.Capabilities.GetRequired<IPageStorage>().KeysAsync()).Count.ToString() %></p>
            """);
        using HttpClient client = CreateClient();
        await client.GetStringAsync("/counter");

        Assert.That(await client.GetStringAsync("/other"), Does.Contain("keys: 0"));
    }
}
