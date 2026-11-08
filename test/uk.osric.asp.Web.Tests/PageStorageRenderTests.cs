// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can reach its state through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageStorageRenderTests {

    private const string Counter = """
        <% IPageStorage storage = ctx.Features.GetRequired<IPageStorage>();
           PageValue<int> stored = await storage.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await storage.SetAsync("visits", visits); %>
        <p>Visited <%= visits.ToString() %> times.</p>
        """;

    [Test]
    public async Task APageKeepsItsStateBetweenRendersAsync() {
        using PageHarness pages = Harness("/counter");
        await pages.StoreCompiledAsync("/counter", Counter);

        (RenderResult first, string firstOutput) = await pages.RenderAsync("/counter");
        (RenderResult second, string secondOutput) = await pages.RenderAsync("/counter");

        Assert.That(first.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(second.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(firstOutput, Does.Contain("Visited 1 times."));
        Assert.That(secondOutput, Does.Contain("Visited 2 times."));
    }

    [Test]
    public async Task AnAwaitedReadInsideAWriteExpressionEmitsItsValueAsync() {
        using PageHarness pages = Harness("/visits");
        pages.State.Seed("/visits", "visits", "7");
        await pages.StoreCompiledAsync("/visits", """
            <p>Visited <%= (await ctx.Features.GetRequired<IPageStorage>()
                .TryGetValueAsync<int>("visits")).Value.ToString() %> times.</p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/visits");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("Visited 7 times."));
    }

    [Test]
    public async Task APageCanRenderWithoutStorageAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/plain", """
            <p><%= ctx.Features.Get<IPageStorage>() is null ? "no storage" : "storage" %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/plain", withStorage: false);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("no storage"));
    }

    [Test]
    public async Task EachPageGetsItsOwnStoreAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.State.LivePages.Add("/first");
        pages.State.LivePages.Add("/second");
        await pages.StoreCompiledAsync("/first", """
            <% await ctx.Features.GetRequired<IPageStorage>().SetAsync("shared", "one"); %>written
            """);
        await pages.StoreCompiledAsync("/second", """
            <p>keys: <%= (await ctx.Features.GetRequired<IPageStorage>().KeysAsync()).Count.ToString() %></p>
            """);

        await pages.RenderAsync("/first");
        (RenderResult result, string output) = await pages.RenderAsync("/second");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("keys: 0"));
    }

    [Test]
    public async Task AHeadRenderCannotWriteStateAsync() {
        using PageHarness pages = Harness("/counter");
        await pages.StoreCompiledAsync("/counter", Counter);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/counter", isHead: true));

        Assert.That(refused?.Message, Does.Contain("HEAD"));
        Assert.That(pages.State.WriteCount, Is.Zero);
    }

    private static PageHarness Harness(string path) {
        PageHarness harness = new(new FakePageStore());
        harness.State.LivePages.Add(path);

        return harness;
    }
}
