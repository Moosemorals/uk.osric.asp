// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>The capability a page reads the store through.</summary>
[TestFixture]
public sealed class PageCatalogScopeTests {

    [Test]
    public async Task AnAnonymousVisitorCanReadTheStoreAsync() {
        (RenderResult result, string output) = await RenderListingAsync(user: null);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("/blog"));
        Assert.That(output, Does.Contain("/notes"));
    }

    [Test]
    public async Task ASignedInVisitorReadsTheSameThingAsync() {
        (RenderResult anonymous, string anonymousOutput) = await RenderListingAsync(user: null);
        (RenderResult signedIn, string signedInOutput) = await RenderListingAsync(new PageUser("owner"));

        Assert.That(signedIn.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(signedInOutput, Is.EqualTo(anonymousOutput));
        Assert.That(anonymous.Status, Is.EqualTo(RenderStatus.Rendered));
    }

    [Test]
    public async Task ALivePageIsReadWithItsHashAndItsSavedTimeAsync() {
        DateTimeOffset savedAt = new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);
        FakePageReadStore store = new();
        string hash = store.Save("/blog", "<h1>Blog</h1>", savedAt);

        PageSource? source = await Scope(store).ReadAsync("/blog");

        Assert.That(source, Is.Not.Null);
        Assert.That(source!.Key, Is.EqualTo("/blog"));
        Assert.That(source.Hash, Is.EqualTo(hash));
        Assert.That(source.SavedAt, Is.EqualTo(savedAt));
        Assert.That(source.Source, Is.EqualTo("<h1>Blog</h1>"));
    }

    [Test]
    public async Task AMissingPageReadsAsNothingAsync() {
        Assert.That(await Scope(new FakePageReadStore()).ReadAsync("/missing"), Is.Null);
    }

    [Test]
    public async Task ATombstonedPageIsListedAndReadsAsNothingAsync() {
        FakePageReadStore store = new();
        store.Save("/gone", "<h1>Gone</h1>");
        store.Delete("/gone");
        PageCatalogScope scope = Scope(store);

        IReadOnlyList<PageEntry> entries = await scope.ListAsync("");

        Assert.That(entries.Single(entry => entry.Key == "/gone").IsDeleted, Is.True);
        Assert.That(await scope.ReadAsync("/gone"), Is.Null);
    }

    [Test]
    public async Task AReservedKeyIsMarkedReservedAsync() {
        FakePageReadStore store = new();
        store.Save("/edit/blog", "reserved");
        store.Save("/blog", "public");

        IReadOnlyList<PageEntry> entries = await Scope(store).ListAsync("");

        Assert.That(entries.Single(entry => entry.Key == "/edit/blog").IsReserved, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/blog").IsReserved, Is.False);
    }

    [Test]
    public async Task ANoCodePageIsMarkedAsAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/site.css", "body { color: red; }", templateHasCode: false);
        store.Save("/index.html", "<!DOCTYPE html><html></html>", templateHasCode: false);

        PageEntry[] entries = [.. await Scope(store).ListAsync("")];

        Assert.That(entries.Single(entry => entry.Key == "/site.css").IsAsset, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/index.html").IsAsset, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/site.css").IsHtml, Is.False);
        Assert.That(entries.Single(entry => entry.Key == "/index.html").IsHtml, Is.True);
    }

    [Test]
    public async Task ACodePageIsNotAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "<h1>Blog</h1>", templateHasCode: true);

        PageEntry entry = (await Scope(store).ListAsync("")).Single();

        Assert.That(entry.IsHtml, Is.True);
        Assert.That(entry.IsAsset, Is.False);
    }

    [Test]
    public async Task TheListingIsOrderedByKeyAsync() {
        FakePageReadStore store = new();
        store.Save("/blog/post", "post");
        store.Save("/about", "about");
        store.Save("/blog", "blog");

        IReadOnlyList<PageEntry> entries = await Scope(store).ListAsync("");

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo((string[])["/about", "/blog", "/blog/post"]));
    }

    [Test]
    public async Task APrefixListsItsSubtreeAndNotItsNeighboursAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/blog/post", "post");
        store.Save("/blogroll", "not a child");

        IReadOnlyList<PageEntry> entries = await Scope(store).ListAsync("/blog");

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo((string[])["/blog", "/blog/post"]));
    }

    [Test]
    public async Task TheRootPrefixListsEverythingAsync() {
        FakePageReadStore store = new();
        store.Save("/about", "about");
        store.Save("/blog", "blog");

        Assert.That((await Scope(store).ListAsync("/")).Count, Is.EqualTo(2));
        Assert.That((await Scope(store).ListAsync("")).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task APrefixIsNormalisedAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/blog/post", "post");

        Assert.That((await Scope(store).ListAsync("/Blog/")).Select(entry => entry.Key),
            Is.EqualTo((string[])["/blog", "/blog/post"]));
    }

    [Test]
    public async Task APrefixThatIsNotAddressableIsRefusedAsync() {
        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Scope(new FakePageReadStore()).ListAsync("/a?b"));

        Assert.That(refused?.Message, Does.Contain("?"));
    }

    [Test]
    public async Task AnUnknownKeyHasNoRevisionsAsync() {
        Assert.That(await Scope(new FakePageReadStore()).RevisionsAsync("/missing"), Is.Empty);
    }

    [Test]
    public async Task AHistoryListsNewestFirstAndMarksTheCurrentRevisionAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "first", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        store.Save("/blog", "second", new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));
        string current = store.Save("/blog", "third", new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));

        IReadOnlyList<PageRevisionInfo> revisions = await Scope(store).RevisionsAsync("/blog");

        Assert.That(revisions.Select(revision => revision.SavedAt), Is.Ordered.Descending);
        Assert.That(revisions.Where(revision => revision.IsCurrent).Count(), Is.EqualTo(1));
        Assert.That(revisions.Single(revision => revision.IsCurrent).Hash, Is.EqualTo(current));
    }

    [Test]
    public async Task ATombstonedPagesHistoryIsStillReadableAsync() {
        FakePageReadStore store = new();
        store.Save("/gone", "first");
        store.Save("/gone", "second");
        store.Delete("/gone");

        IReadOnlyList<PageRevisionInfo> revisions = await Scope(store).RevisionsAsync("/gone");

        Assert.That(revisions, Has.Count.EqualTo(2));
        Assert.That(revisions.Any(revision => revision.IsCurrent), Is.False);
    }

    [Test]
    public async Task ARevisionIsReadByItsKeyAndHashAsync() {
        FakePageReadStore store = new();
        string first = store.Save("/blog", "first");

        PageSource? source = await Scope(store).ReadRevisionAsync("/blog", first);

        Assert.That(source, Is.Not.Null);
        Assert.That(source!.Hash, Is.EqualTo(first));
        Assert.That(source.Source, Is.EqualTo("first"));
    }

    [Test]
    public async Task AHashFromOnePageIsNotReadUnderAnotherAsync() {
        FakePageReadStore store = new();
        string hash = store.Save("/one", "one");
        store.Save("/two", "two");

        Assert.That(await Scope(store).ReadRevisionAsync("/two", hash), Is.Null);
    }

    [Test]
    public async Task OneRowPastTheCapIsRefusedAsync() {
        FakePageReadStore store = new();
        store.Save("/one", "1");
        store.Save("/two", "2");
        store.Save("/three", "3");
        PageCatalogScope scope = Scope(store, options: new PageReadOptions { MaxRowsPerList = 2 });

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ListAsync(""));

        Assert.That(refused?.Message, Does.Contain("2").And.Contain("prefix"));
    }

    [Test]
    public async Task TheReadBudgetIsSharedByTheChainAsync() {
        FakePageReadStore store = new();
        store.Save("/page", "123456");
        store.Save("/layout", "123456");
        ReadBudget budget = new(10);
        PageCatalogScope page = Scope(store, budget: budget);
        PageCatalogScope layout = Scope(store, budget: budget);

        Assert.That(await page.ReadAsync("/page"), Is.Not.Null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => layout.ReadAsync("/layout"));
    }

    [Test]
    public async Task ASourcePastTheBudgetIsRefusedAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", new string('x', 20));
        PageCatalogScope scope = Scope(store, budget: new ReadBudget(10));

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ReadAsync("/blog"));

        Assert.That(refused?.Message, Does.Contain("MaxSourceCharsPerRender"));
    }

    [Test]
    public async Task AnAbandonedRenderCannotReadAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "source");
        PageCatalogScope scope = Scope(store);

        scope.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ListAsync(""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ReadAsync("/blog"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.RevisionsAsync("/blog"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ReadRevisionAsync("/blog", "hash"));
    }

    [Test]
    public async Task TheDeadlineCancelsAReadAsync() {
        FakePageReadStore store = new() { ReadDelay = TimeSpan.FromSeconds(30) };
        store.Save("/blog", "source");
        PageCatalogScope scope = Scope(store);
        using CancellationTokenSource deadline = new();
        deadline.Cancel();
        scope.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.ListAsync(""));
    }

    [Test]
    public async Task TheDeadlineIsPassedToTheStoreAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "source");
        PageCatalogScope scope = Scope(store);
        using CancellationTokenSource deadline = new();
        scope.AttachDeadline(deadline.Token);

        await scope.ReadAsync("/blog");

        Assert.That(store.LastCancellationToken, Is.EqualTo(deadline.Token));
    }

    private static PageCatalogScope Scope(
        FakePageReadStore store,
        PageReadOptions? options = null,
        ReadBudget? budget = null) {
        PageReadOptions read = options ?? new PageReadOptions();

        return new PageCatalogScope(
            PagePath.Normalize("/test"),
            store,
            read,
            budget ?? new ReadBudget(read.MaxSourceCharsPerRender),
            new ReservedPaths(ReservedPaths.Default),
            TestContentTypes.Shipped);
    }

    private static async Task<(RenderResult Result, string Output)> RenderListingAsync(PageUser? user) {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/notes", "notes");

        using PageHarness pages = new(new FakePageStore(), readStore: store);
        await pages.StoreCompiledAsync("/listing", """
            <% IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
               System.Collections.Generic.IReadOnlyList<PageEntry> entries = await catalog.ListAsync(""); %>
            <% foreach (PageEntry entry in entries) { %><p><%= entry.Key %></p><% } %>
            """);

        return await pages.RenderAsync("/listing", user: user);
    }
}
