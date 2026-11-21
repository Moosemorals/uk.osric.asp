// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;
using PageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>The capability a page reads the store through.</summary>
[TestFixture]
public sealed class PageCatalogScopeTests {

    /// <summary>The build every listing in this fixture is judged against.</summary>
    private const string CurrentRuntimeId = "build-1";


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
    public async Task AListingSaysWhatTheRowIsRelativeToTheSeedAsync() {
        SeedFile about = TestOptions.Seeds.Find(PagePath.Normalize("/about"))!;
        FakePageReadStore store = new();
        store.Save("/about", about.Source, seedHash: about.Hash);

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "/");

        PageEntry entry = entries.Single(item => item.Key == "/about");
        Assert.Multiple(() => {
            Assert.That(entry.Seed?.Verdict, Is.EqualTo("in sync"));
            Assert.That(entry.Seed?.CanApply, Is.False);
            Assert.That(entry.Seed?.CanAdopt, Is.False);
        });
    }

    [Test]
    public async Task APageWithNoSeedEitherSideCarriesNoSummaryAsync() {
        FakePageReadStore store = new();
        store.Save("/mine", "mine");

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "/");

        Assert.That(entries.Single(item => item.Key == "/mine").Seed, Is.Null);
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

        IReadOnlyList<PageEntry> entries = await ListAsync(scope, "");

        Assert.That(entries.Single(entry => entry.Key == "/gone").IsDeleted, Is.True);
        Assert.That(await scope.ReadAsync("/gone"), Is.Null);
    }

    [Test]
    public async Task AReservedKeyIsMarkedReservedAsync() {
        FakePageReadStore store = new();
        store.Save("/admin/scrub/blog", "reserved");
        store.Save("/blog", "public");

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "");

        Assert.That(entries.Single(entry => entry.Key == "/admin/scrub/blog").IsReserved, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/blog").IsReserved, Is.False);
    }

    [Test]
    public async Task AWorkerIsMarkedAsOneAndIsNotAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/_work/echo", "<%! public sealed class Echo : IWorker { } %>",
            templateKind: PageKind.Worker, runtimeId: CurrentRuntimeId, hasImage: true);

        PageEntry entry = (await ListAsync(Scope(store), "")).Single();

        Assert.Multiple(() => {
            Assert.That(entry.IsWorker, Is.True);
            Assert.That(entry.IsLibrary, Is.False);
            Assert.That(entry.IsAsset, Is.False);
            Assert.That(entry.State, Is.EqualTo("Worker"));
        });
    }

    [Test]
    public async Task ANoCodePageIsMarkedAsAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/site.css", "body { color: red; }", templateKind: PageKind.Text);
        store.Save("/index.html", "<!DOCTYPE html><html></html>", templateKind: PageKind.Text);

        PageEntry[] entries = [.. await ListAsync(Scope(store), "")];

        Assert.That(entries.Single(entry => entry.Key == "/site.css").IsAsset, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/index.html").IsAsset, Is.True);
        Assert.That(entries.Single(entry => entry.Key == "/site.css").IsHtml, Is.False);
        Assert.That(entries.Single(entry => entry.Key == "/index.html").IsHtml, Is.True);
    }

    [Test]
    public async Task ACodePageIsNotAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "<h1>Blog</h1>", templateKind: PageKind.Page);

        PageEntry entry = (await ListAsync(Scope(store), "")).Single();

        Assert.That(entry.IsHtml, Is.True);
        Assert.That(entry.IsAsset, Is.False);
    }

    [Test]
    public async Task ALibraryIsMarkedAsOneAndIsNotAnAssetAsync() {
        FakePageReadStore store = new();
        store.Save("/_lib/document", "<%! class Document { } %>",
            templateKind: PageKind.Library, runtimeId: CurrentRuntimeId);

        PageEntry entry = (await ListAsync(Scope(store), "")).Single();

        Assert.That(entry.IsLibrary, Is.True);
        Assert.That(entry.IsAsset, Is.False);
        Assert.That(entry.State, Is.EqualTo("Library"));
    }

    [Test]
    public async Task TheListingIsOrderedByKeyAsync() {
        FakePageReadStore store = new();
        store.Save("/blog/post", "post");
        store.Save("/about", "about");
        store.Save("/blog", "blog");

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "");

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(["/about", "/blog", "/blog/post"]));
    }

    [Test]
    public async Task APrefixListsItsSubtreeAndNotItsNeighboursAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/blog/post", "post");
        store.Save("/blogroll", "not a child");

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "/blog");

        Assert.That(entries.Select(entry => entry.Key), Is.EqualTo(["/blog", "/blog/post"]));
    }

    [Test]
    public async Task TheRootPrefixListsEverythingAsync() {
        FakePageReadStore store = new();
        store.Save("/about", "about");
        store.Save("/blog", "blog");

        Assert.That((await ListAsync(Scope(store), "/")).Count, Is.EqualTo(2));
        Assert.That((await ListAsync(Scope(store), "")).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task APrefixIsNormalisedAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/blog/post", "post");

        Assert.That((await ListAsync(Scope(store), "/Blog/")).Select(entry => entry.Key),
            Is.EqualTo(["/blog", "/blog/post"]));
    }

    [Test]
    public async Task APrefixThatIsNotAddressableIsRefusedAsync() {
        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Scope(new FakePageReadStore()).ListAsync(new("/a?b")));

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
    public async Task AFullSliceSaysThereIsMoreAsync() {
        FakePageReadStore store = new();
        store.Save("/one", "1");
        store.Save("/two", "2");
        store.Save("/three", "3");
        PageCatalogScope scope = Scope(store, options: TestOptions.Read with { MaxRowsPerList = 2 });

        PageListing listing = await scope.ListAsync(new());

        string[] listed = [.. listing.Entries.Select(entry => entry.Key)];
        string[] expected = ["/one", "/three"];
        Assert.Multiple(() => {
            Assert.That(listed, Is.EqualTo(expected));
            Assert.That(listing.Truncated, Is.True);
        });
    }

    [Test]
    public async Task AFirstCallAnswersTheTokenAndNoChangeAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");

        PageListing listing = await Scope(store).ListAsync(new());

        Assert.Multiple(() => {
            Assert.That(listing.Version, Is.Not.Empty);
            Assert.That(listing.Changed, Is.False, "a call that carried no token is never changed");
        });
    }

    [Test]
    public async Task ACallCarryingTheTokenSeesNoChangeUntilTheStoreMovesAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        PageListing first = await Scope(store).ListAsync(new());

        PageListing same = await Scope(store).ListAsync(new(Version: first.Version));
        store.Save("/blog", "newer");
        PageListing moved = await Scope(store).ListAsync(new(Version: first.Version));

        Assert.Multiple(() => {
            Assert.That(same.Changed, Is.False);
            Assert.That(moved.Changed, Is.True);
        });
    }

    [Test]
    public async Task ATokenFromAnotherPrefixIsAChangeRatherThanAnErrorAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/notes", "notes");
        PageListing elsewhere = await Scope(store).ListAsync(new("/notes"));

        PageListing listing = await Scope(store).ListAsync(new("/blog", Version: elsewhere.Version));

        Assert.That(listing.Changed, Is.True);
    }

    [Test]
    public async Task ANonsenseTokenIsAChangeRatherThanAnErrorAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");

        PageListing listing = await Scope(store).ListAsync(new(Version: "not a token"));

        Assert.That(listing.Changed, Is.True);
    }

    [Test]
    public async Task AListingRowSaysWhichLibrariesItsClosureNamedAsync() {
        FakePageReadStore store = new();
        store.Save("/uses", "uses", templateLibraries: PageLibraryFingerprint.Of([
            ("/_lib/document", "hash-d"),
        ]));
        store.Save("/plain", "plain");

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store));

        string[] closure = ["/_lib/document"];
        Assert.Multiple(() => {
            Assert.That(entries.Single(entry => entry.Key == "/uses").Libraries, Is.EqualTo(closure));
            Assert.That(entries.Single(entry => entry.Key == "/plain").Libraries, Is.Empty);
        });
    }

    [Test]
    public async Task ACursorWalksTheStoreWithoutRepeatingOrMissingAKeyAsync() {
        FakePageReadStore store = new();
        foreach (string key in new[] { "/a", "/b", "/c", "/d", "/e" }) {
            store.Save(key, key);
        }

        PageCatalogScope scope = Scope(store, options: TestOptions.Read with { MaxRowsPerList = 2 });
        List<string> keys = [];
        string? after = null;
        for (int page = 0; page < 10; page++) {
            PageListing listing = await scope.ListAsync(new(After: after, Limit: 2));
            keys.AddRange(listing.Entries.Select(entry => entry.Key));
            if (!listing.Truncated) {
                break;
            }

            after = listing.Entries[^1].Key;
        }

        string[] expected = ["/a", "/b", "/c", "/d", "/e"];
        Assert.Multiple(() => {
            Assert.That(keys, Is.EqualTo(expected));
            Assert.That(keys, Is.Unique);
        });
    }

    [Test]
    public async Task ACursorPastTheLastKeyIsAnEmptyAnswerAsync() {
        FakePageReadStore store = new();
        store.Save("/one", "1");
        store.Save("/two", "2");

        PageListing listing = await Scope(store).ListAsync(new(After: "/zzz"));

        Assert.Multiple(() => {
            Assert.That(listing.Entries, Is.Empty);
            Assert.That(listing.Truncated, Is.False);
        });
    }

    [Test]
    public async Task ACursorIsNormalisedLikeAPrefixAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "1");
        store.Save("/blog/post", "2");
        store.Save("/notes", "3");

        PageListing listing = await Scope(store).ListAsync(new(After: "/Blog/"));

        string[] listed = [.. listing.Entries.Select(entry => entry.Key)];
        string[] expected = ["/blog/post", "/notes"];
        Assert.That(listed, Is.EqualTo(expected));
    }

    [Test]
    public async Task ALimitPastTheCapIsRefusedAsync() {
        FakePageReadStore store = new();
        store.Save("/one", "1");
        PageCatalogScope scope = Scope(store, options: TestOptions.Read with { MaxRowsPerList = 2 });

        ArgumentOutOfRangeException? refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => scope.ListAsync(new(Limit: 3)));

        Assert.That(refused?.Message, Does.Contain("Pages:Read:MaxRowsPerList"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task ALimitBelowOneRowIsRefusedAsync(int limit) {
        FakePageReadStore store = new();
        store.Save("/one", "1");

        Assert.That(
            () => Scope(store).ListAsync(new(Limit: limit)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
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
    public void TheShellLinksAreTheValuesTheChainBuilt() {
        PageCatalogScope scope = Scope(new FakePageReadStore());

        Assert.That(scope.ShellLinks(), Is.EqualTo(ShellCatalogue.Links("/test", string.Empty, "/pages")));
    }

    [Test]
    public void AnAbandonedRenderCannotReadTheShellLinks() {
        PageCatalogScope scope = Scope(new FakePageReadStore());

        scope.Abandon();

        Assert.Throws<InvalidOperationException>(() => scope.ShellLinks());
    }

    [Test]
    public async Task AnAbandonedRenderCannotReadAsync() {
        FakePageReadStore store = new();
        store.Save("/blog", "source");
        PageCatalogScope scope = Scope(store);

        scope.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ListAsync(new()));
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
        await deadline.CancelAsync();
        scope.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.ListAsync(new()));
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

    [Test]
    public async Task AListingReportsTheHostsImageStateAsync() {
        FakePageReadStore store = new();
        store.Save("/compiled", "source", runtimeId: CurrentRuntimeId, hasImage: true);
        store.Save("/warned", "source", runtimeId: CurrentRuntimeId, hasImage: true, hasDiagnostics: true);
        store.Save("/stale", "source", runtimeId: "build-0", hasImage: true);
        store.Save("/failed", "source", runtimeId: CurrentRuntimeId);
        store.Save("/never", "source");
        store.Save("/sheet.css", "source", templateKind: PageKind.Text, runtimeId: CurrentRuntimeId, hasImage: true);

        IReadOnlyList<PageEntry> entries = await ListAsync(Scope(store), "/");

        Assert.Multiple(() => {
            Assert.That(StateOf(entries, "/compiled"), Is.EqualTo("Compiled"));
            Assert.That(StateOf(entries, "/warned"), Is.EqualTo("Compiled with warnings"));
            Assert.That(StateOf(entries, "/stale"), Is.EqualTo("Stale"));
            Assert.That(StateOf(entries, "/failed"), Is.EqualTo("Compile failed"));
            Assert.That(StateOf(entries, "/never"), Is.EqualTo("Not compiled"));
            Assert.That(StateOf(entries, "/sheet.css"), Is.EqualTo("Stored as source"));
            Assert.That(store.ReadCount, Is.EqualTo(1), "the state comes from the listing, not a second read");
        });
    }

    private static string StateOf(IReadOnlyList<PageEntry> entries, string key) =>
        entries.Single(entry => entry.Key == key).State;

    private static async Task<IReadOnlyList<PageEntry>> ListAsync(
        PageCatalogScope scope, string prefix = "", string? after = null, int? limit = null) =>
        (await scope.ListAsync(new PageListingRequest(prefix, after, limit))).Entries;

    private static PageCatalogScope Scope(
        FakePageReadStore store,
        PageReadOptions? options = null,
        ReadBudget? budget = null) {
        PageReadOptions read = options ?? TestOptions.Read;

        return new PageCatalogScope(
            PagePath.Normalize("/test"),
            ShellCatalogue.Links("/test", string.Empty, "/pages"),
            store,
            read,
            budget ?? new ReadBudget(read.MaxSourceCharsPerRender),
            new ReservedPaths(ReservedPaths.Default),
            TestContentTypes.Shipped,
            TestOptions.Seeds,
            CurrentRuntimeId);
    }

    private static async Task<(RenderResult Result, string Output)> RenderListingAsync(PageUser? user) {
        FakePageReadStore store = new();
        store.Save("/blog", "blog");
        store.Save("/notes", "notes");

        using PageHarness pages = new(new FakePageStore(), readStore: store);
        await pages.StoreCompiledAsync("/listing", """
            <% IPageCatalog catalog = ctx.Capabilities.GetRequired<IPageCatalog>();
               System.Collections.Generic.IReadOnlyList<PageEntry> entries = (await catalog.ListAsync(new())).Entries; %>
            <% foreach (PageEntry entry in entries) { %><p><%= entry.Key %></p><% } %>
            """);

        return await pages.RenderAsync("/listing", user: user);
    }
}
