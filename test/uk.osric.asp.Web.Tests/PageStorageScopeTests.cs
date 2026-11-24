// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageStorageScopeTests {

    [Test]
    public async Task AScalarRoundTripsAsync() {
        IValueStore store = Scope();

        await store.SetAsync("count", 42);
        PageValue<int> stored = await store.TryGetValueAsync<int>("count");

        Assert.That(stored.Found, Is.True);
        Assert.That(stored.Value, Is.EqualTo(42));
    }

    [Test]
    public async Task ADeclarationBlockRecordRoundTripsAsync() {
        IValueStore store = Scope();

        await store.SetAsync("entry", new Entry("first", 3));
        PageValue<Entry> stored = await store.TryGetValueAsync<Entry>("entry");

        Assert.That(stored.Value, Is.EqualTo(new Entry("first", 3)));
    }

    [Test]
    public async Task AListRoundTripsAsync() {
        IValueStore store = Scope();

        await store.SetAsync("items", new List<string> { "a", "b" });
        PageValue<List<string>> stored = await store.TryGetValueAsync<List<string>>("items");

        Assert.That(stored.Value, Is.EqualTo(new List<string> { "a", "b" }));
    }

    [Test]
    public async Task AnEnumRoundTripsAsItsNameAsync() {
        IValueStore store = Scope();

        await store.SetAsync("mode", DayOfWeek.Friday);
        PageValue<DayOfWeek> stored = await store.TryGetValueAsync<DayOfWeek>("mode");

        Assert.That(stored.Value, Is.EqualTo(DayOfWeek.Friday));
    }

    [Test]
    public async Task AMissingKeyIsNotFoundAsync() {
        IValueStore store = Scope();

        PageValue<int> stored = await store.TryGetValueAsync<int>("absent");

        Assert.That(stored.Found, Is.False);
        Assert.That(stored.Value, Is.Zero);
        Assert.That(stored.Lifetime, Is.EqualTo(StoreLifetime.Default));
        Assert.That(stored.Page, Is.Null);
    }

    [Test]
    public async Task AStoredNullIsFoundWithANullValueAsync() {
        IValueStore store = Scope();

        await store.SetAsync<Entry?>("last", null);
        PageValue<Entry?> stored = await store.TryGetValueAsync<Entry?>("last");

        Assert.That(stored.Found, Is.True);
        Assert.That(stored.Value, Is.Null);
    }

    [Test]
    public async Task AReadAnswersTheLifetimeAndThePageThatWroteItAsync() {
        IValueStore store = Scope();

        await store.SetAsync("key", 1, StoreLifetime.NextRead);

        PageValue<int> stored = await store.TryGetValueAsync<int>("key");
        Assert.Multiple(() => {
            Assert.That(stored.Lifetime.Kind, Is.EqualTo(StoreLifetimeKind.NextRead));
            Assert.That(stored.Page, Is.EqualTo("/page"));
        });
    }

    [Test]
    public async Task KeysAreOrdinalAndDeleteReportsWhatExistedAsync() {
        IValueStore store = Scope();
        await store.SetAsync("b", 1);
        await store.SetAsync("A", 2);
        await store.SetAsync("a", 3);

        Assert.That(Keys(await store.KeysAsync()), Is.EqualTo(new List<string> { "A", "a", "b" }));
        Assert.That(await store.DeleteAsync("a"), Is.True);
        Assert.That(await store.DeleteAsync("a"), Is.False);
        Assert.That(Keys(await store.KeysAsync()), Is.EqualTo(new List<string> { "A", "b" }));
    }

    [Test]
    public async Task PagesCannotReadEachOthersKeysThroughTheirOwnHandleAsync() {
        FakeStoreValues store = Live();
        store.LivePages.Add("/first");
        store.LivePages.Add("/second");
        IValueStore first = Page(store, "/first");
        IValueStore second = Page(store, "/second");

        await first.SetAsync("shared", "one");

        Assert.That((await second.TryGetValueAsync<string>("shared")).Found, Is.False);
        Assert.That(await second.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task ACrossPageReadFindsWhatTheOtherPageLeftAsync() {
        FakeStoreValues store = Live();
        store.LivePages.Add("/other");
        IValueStore first = Page(store, "/page");

        await Page(store, "/other").SetAsync("shared", "one");

        PageValue<string> stored = await first.TryGetValueAsync<string>("/other", "shared");
        Assert.Multiple(() => {
            Assert.That(stored.Found, Is.True);
            Assert.That(stored.Value, Is.EqualTo("one"));
            Assert.That(stored.Page, Is.EqualTo("/other"));
        });
    }

    [Test]
    public async Task ATakeConsumesTheOneShotAndASecondTakeFindsNothingAsync() {
        IValueStore store = Scope();
        await store.SetAsync("note", "saved", StoreLifetime.NextRead);

        Assert.That((await store.TryTakeAsync<string>("note")).Value, Is.EqualTo("saved"));
        Assert.That((await store.TryTakeAsync<string>("note")).Found, Is.False);
    }

    [Test]
    public async Task AVisitorLifetimeIsRefusedOnAPageAsync() {
        IValueStore store = Scope();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("key", 1, StoreLifetime.Session));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("key", 1, StoreLifetime.Browser));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("key", 1, StoreLifetime.Person));
    }

    [Test]
    public async Task AKeyLongerThanTheLimitIsRefusedAsync() {
        FakeStoreValues store = Live();
        PageStoresOptions options = TestOptions.Stores with { MaxKeyBytes = 4 };
        IValueStore values = new PageValueStore(Key("/page"), store, options.Page, options.MaxKeyBytes, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => values.SetAsync("12345", 1));
    }

    [TestCase("line\nbreak")]
    [TestCase("tab\tkey")]
    public async Task AKeyWithAControlCharacterIsRefusedAsync(string key) {
        IValueStore store = Scope();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(key, 1));
    }

    [Test]
    public async Task AValueLargerThanTheLimitIsRefusedAsync() {
        FakeStoreValues store = Live();
        PageStoresOptions options = TestOptions.Stores with {
            Page = TestOptions.Stores.Page with { MaxValueBytes = 16, MaxTotalBytes = 16 },
        };
        IValueStore values = new PageValueStore(Key("/page"), store, options.Page, options.MaxKeyBytes, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => values.SetAsync("big", new string('x', 64)));
    }

    [Test]
    public async Task AWriteThatBreaksThePageByteLimitIsRefusedAsync() {
        FakeStoreValues store = Live();
        PageStoresOptions options = TestOptions.Stores with {
            Page = TestOptions.Stores.Page with { MaxValueBytes = 1024, MaxTotalBytes = 64 },
        };
        IValueStore values = new PageValueStore(Key("/page"), store, options.Page, options.MaxKeyBytes, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => values.SetAsync("first", new string('x', 100)));
    }

    [Test]
    public async Task AWritePastTheKeyCountLimitIsRefusedAsync() {
        FakeStoreValues store = Live();
        PageStoresOptions options = TestOptions.Stores with {
            Page = TestOptions.Stores.Page with { MaxKeys = 2 },
        };
        IValueStore values = new PageValueStore(Key("/page"), store, options.Page, options.MaxKeyBytes, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(async () => {
            await values.SetAsync("one", 1);
            await values.SetAsync("two", 2);
            await values.SetAsync("three", 3);
        });
    }

    [Test]
    public async Task AWriteToADeletedPageIsRefusedAsync() {
        FakeStoreValues store = new();
        IValueStore values = Page(store, "/gone");

        await Assert.ThrowsAsync<ArgumentException>(() => values.SetAsync("key", 1));
    }

    [Test]
    public async Task AStoredValueOfTheWrongShapeThrowsRatherThanReportingAbsentAsync() {
        FakeStoreValues store = Live();
        store.Seed("/page", "entry", "\"text\"");
        IValueStore values = Page(store, "/page");

        await Assert.ThrowsAsync<JsonException>(() => values.TryGetValueAsync<Entry>("entry"));
    }

    [Test]
    public async Task BadJsonThrowsRatherThanReportingAbsentAsync() {
        FakeStoreValues store = Live();
        store.Seed("/page", "entry", "{not json");
        IValueStore values = Page(store, "/page");

        await Assert.ThrowsAsync<JsonException>(() => values.TryGetValueAsync<Entry>("entry"));
    }

    [Test]
    public async Task AHeadRenderMayReadButNotWriteAsync() {
        FakeStoreValues store = Live();
        store.Seed("/page", "visits", "1");
        PageValueStore values = new(Key("/page"), store, TestOptions.Stores.Page, TestOptions.Stores.MaxKeyBytes, writable: false);

        Assert.That((await values.TryGetValueAsync<int>("visits")).Value, Is.EqualTo(1));
        Assert.That(await values.KeysAsync(), Is.Not.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => values.SetAsync("visits", 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => values.DeleteAsync("visits"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => values.TryTakeAsync<int>("visits"));
    }

    [Test]
    public async Task AnAbandonedRenderCannotUseStorageAsync() {
        PageValueStore values = new(Key("/page"), Live(), TestOptions.Stores.Page, TestOptions.Stores.MaxKeyBytes, writable: true);

        values.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => values.SetAsync("key", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => values.TryGetValueAsync<int>("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => values.KeysAsync());
    }

    [Test]
    public async Task TheRenderDeadlineIsPassedToTheStoreAsync() {
        FakeStoreValues store = Live();
        PageValueStore values = new(Key("/page"), store, TestOptions.Stores.Page, TestOptions.Stores.MaxKeyBytes, writable: true);
        using CancellationTokenSource deadline = new();
        values.AttachDeadline(deadline.Token);

        await values.SetAsync("key", 1);

        Assert.That(store.LastCancellationToken, Is.EqualTo(deadline.Token));
    }

    [Test]
    public async Task AnAlreadyCancelledDeadlineRefusesTheCallAsync() {
        PageValueStore values = new(Key("/page"), Live(), TestOptions.Stores.Page, TestOptions.Stores.MaxKeyBytes, writable: true);
        using CancellationTokenSource deadline = new();
        await deadline.CancelAsync();
        values.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => values.SetAsync("key", 1));
    }

    [Test]
    public async Task TheLastWriteWinsAsync() {
        IValueStore store = Scope();

        await store.SetAsync("key", "first");
        await store.SetAsync("key", "second");

        Assert.That((await store.TryGetValueAsync<string>("key")).Value, Is.EqualTo("second"));
    }

    private static List<string> Keys(IReadOnlyList<StoreEntry> entries) =>
        [.. entries.Select(entry => entry.Key)];

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private static IValueStore Scope() => Page(Live(), "/page");

    private static IValueStore Page(FakeStoreValues store, string path) =>
        new PageValueStore(Key(path), store, TestOptions.Stores.Page, TestOptions.Stores.MaxKeyBytes, writable: true);

    private static FakeStoreValues Live() {
        FakeStoreValues store = new();
        store.LivePages.Add("/page");

        return store;
    }

    private sealed record Entry(string Name, int Count);
}
