// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can reach its session through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageSessionRenderTests {

    private const string Flash = """
        <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
           if (ctx.Request.Method == "POST") {
               await session.SetAsync("notice", "saved");
               ctx.Redirect(ctx.Path, 303);
               return;
           }
           PageValue<string> notice = await session.TryGetValueAsync<string>("notice"); %>
        <p><%= notice.Found ? notice.Value! : "nothing yet" %></p>
        """;

    [Test]
    public async Task APageKeepsItsOwnSessionValueBetweenRendersAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/counter", """
            <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
               PageValue<int> stored = await session.TryGetValueAsync<int>("visits");
               int visits = (stored.Found ? stored.Value : 0) + 1;
               await session.SetAsync("visits", visits); %>
            <p>Visited <%= visits.ToString() %> times.</p>
            """);

        (RenderResult first, string firstOutput) = await pages.RenderAsync("/counter");
        (RenderResult second, string secondOutput) = await pages.RenderAsync("/counter");

        Assert.That(first.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(firstOutput, Does.Contain("Visited 1 times."));
        Assert.That(secondOutput, Does.Contain("Visited 2 times."));
    }

    [Test]
    public async Task TwoVisitorsSeeDifferentValuesAsync() {
        FakePageSessionStore store = new();
        using PageHarness first = new(new FakePageStore(), sessionStore: store);
        using PageHarness second = new(new FakePageStore(), sessionStore: store);
        const string page = """
            <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
               PageValue<string> stored = await session.TryGetValueAsync<string>("name"); %>
            <p><%= stored.Found ? stored.Value! : "empty" %></p>
            """;
        await first.StoreCompiledAsync("/page", page);
        await second.StoreCompiledAsync("/page", page);
        store.SeedText(first.Session.Id, "/page", "name", "visitor");

        (_, string firstOutput) = await first.RenderAsync("/page");
        (_, string secondOutput) = await second.RenderAsync("/page");

        Assert.That(firstOutput, Does.Contain("visitor"));
        Assert.That(secondOutput, Does.Contain("empty"), "one visitor's value is invisible to another");
    }

    [Test]
    public async Task APageReadsAnotherPagesValueByPathAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.Sessions.Seed(pages.Session.Id, "/other", "draft", "\"left for you\"");
        await pages.StoreCompiledAsync("/page", """
            <% IPageSession session = ctx.Capabilities.GetRequired<IPageSession>();
               PageValue<string> left = await session.TryGetValueAsync<string>("/other", "draft"); %>
            <p><%= left.Value! %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/page");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("left for you"));
    }

    [Test]
    public async Task ARedirectAfterAWriteIsServedAndTheNextRenderShowsTheValueAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/flash", Flash);

        (RenderResult before, string beforeOutput) = await pages.RenderAsync("/flash");
        (RenderResult write, _) = await RenderPostAsync(pages, "/flash");
        (RenderResult after, string afterOutput) = await pages.RenderAsync("/flash");

        Assert.That(beforeOutput, Does.Contain("nothing yet"));
        Assert.That(write.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(after.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(afterOutput, Does.Contain("saved"));
    }

    [Test]
    public async Task AHeadRenderCannotWriteItsSessionAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/counter", """
            <% await ctx.Capabilities.GetRequired<IPageSession>().SetAsync("visits", 1); %>written
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/counter", isHead: true));

        Assert.That(refused?.Message, Does.Contain("this render"));
        Assert.That(pages.Sessions.ValueCount, Is.Zero);
    }

    [Test]
    public async Task APageCanRenderWithoutASessionAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/plain", """
            <p><%= ctx.Capabilities.Get<IPageSession>() is null ? "no session" : "session" %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/plain", withSession: false);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("no session"));
    }

    private static async Task<(RenderResult Result, string Output)> RenderPostAsync(PageHarness pages, string path) {
        SimpleContext context = pages.CreateContext(PageHarness.Key(path), method: "POST");
        RenderResult result = await pages.Renderer.RenderAsync(PageHarness.Key(path), context, CancellationToken.None);

        return (result, context.GetBody());
    }
}
