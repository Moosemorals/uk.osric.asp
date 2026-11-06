// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can reach its state through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageStorageRenderTests {

    private const string Counter = """
        <% PageValue<int> stored = await ctx.Storage.TryGetValueAsync<int>("visits");
           int visits = (stored.Found ? stored.Value : 0) + 1;
           await ctx.Storage.SetAsync("visits", visits); %>
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
            <p>Visited <%= (await ctx.Storage.TryGetValueAsync<int>("visits")).Value.ToString() %> times.</p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/visits");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("Visited 7 times."));
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
