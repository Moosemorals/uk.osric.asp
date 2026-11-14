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
        IPageStorage storage = Scope();

        await storage.SetAsync("count", 42);
        PageValue<int> stored = await storage.TryGetValueAsync<int>("count");

        Assert.That(stored.Found, Is.True);
        Assert.That(stored.Value, Is.EqualTo(42));
    }

    [Test]
    public async Task ADeclarationBlockRecordRoundTripsAsync() {
        IPageStorage storage = Scope();

        await storage.SetAsync("entry", new Entry("first", 3));
        PageValue<Entry> stored = await storage.TryGetValueAsync<Entry>("entry");

        Assert.That(stored.Value, Is.EqualTo(new Entry("first", 3)));
    }

    [Test]
    public async Task AListRoundTripsAsync() {
        IPageStorage storage = Scope();

        await storage.SetAsync("items", new List<string> { "a", "b" });
        PageValue<List<string>> stored = await storage.TryGetValueAsync<List<string>>("items");

        Assert.That(stored.Value, Is.EqualTo(new List<string> { "a", "b" }));
    }

    [Test]
    public async Task AnEnumRoundTripsAsItsNameAsync() {
        IPageStorage storage = Scope();

        await storage.SetAsync("mode", DayOfWeek.Friday);
        PageValue<DayOfWeek> stored = await storage.TryGetValueAsync<DayOfWeek>("mode");

        Assert.That(stored.Value, Is.EqualTo(DayOfWeek.Friday));
    }

    [Test]
    public async Task AMissingKeyIsNotFoundAsync() {
        IPageStorage storage = Scope();

        PageValue<int> stored = await storage.TryGetValueAsync<int>("absent");

        Assert.That(stored.Found, Is.False);
        Assert.That(stored.Value, Is.Zero);
    }

    [Test]
    public async Task AStoredNullIsFoundWithANullValueAsync() {
        IPageStorage storage = Scope();

        await storage.SetAsync<Entry?>("last", null);
        PageValue<Entry?> stored = await storage.TryGetValueAsync<Entry?>("last");

        Assert.That(stored.Found, Is.True);
        Assert.That(stored.Value, Is.Null);
    }

    [Test]
    public async Task KeysAreOrdinalAndDeleteReportsWhatExistedAsync() {
        IPageStorage storage = Scope();
        await storage.SetAsync("b", 1);
        await storage.SetAsync("A", 2);
        await storage.SetAsync("a", 3);

        Assert.That(await storage.KeysAsync(), Is.EqualTo(new List<string> { "A", "a", "b" }));
        Assert.That(await storage.DeleteAsync("a"), Is.True);
        Assert.That(await storage.DeleteAsync("a"), Is.False);
        Assert.That(await storage.KeysAsync(), Is.EqualTo(new List<string> { "A", "b" }));
    }

    [Test]
    public async Task PagesCannotReadEachOthersKeysAsync() {
        FakePageStateStore store = Live();
        store.LivePages.Add("/first");
        store.LivePages.Add("/second");
        IPageStorage first = new PageStorageScope(Key("/first"), store, TestOptions.Storage, writable: true);
        IPageStorage second = new PageStorageScope(Key("/second"), store, TestOptions.Storage, writable: true);

        await first.SetAsync("shared", "one");

        Assert.That((await second.TryGetValueAsync<string>("shared")).Found, Is.False);
        Assert.That(await second.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task AKeyLongerThanTheCapIsRefusedAsync() {
        PageStorageOptions options = TestOptions.Storage with { MaxKeyChars = 4 };
        IPageStorage storage = new PageStorageScope(Key("/page"), Live(), options, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync("12345", 1));
    }

    [TestCase("line\nbreak")]
    [TestCase("tab\tkey")]
    public async Task AKeyWithAControlCharacterIsRefusedAsync(string key) {
        IPageStorage storage = Scope();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync(key, 1));
    }

    [Test]
    public async Task AValueLargerThanTheCapIsRefusedAsync() {
        PageStorageOptions options = TestOptions.Storage with { MaxValueBytes = 16 };
        IPageStorage storage = new PageStorageScope(Key("/page"), Live(), options, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync("big", new string('x', 64)));
    }

    [Test]
    public async Task AWriteThatBreaksThePageByteCapIsRefusedAsync() {
        PageStorageOptions options = TestOptions.Storage with { MaxValueBytes = 1024, MaxPageBytes = 64 };
        PagePath path = Key("/page");
        FakePageStateStore store = Live(options);
        IPageStorage storage = new PageStorageScope(path, store, options, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync("first", new string('x', 100)));
    }

    [Test]
    public async Task AWritePastTheKeyCountCapIsRefusedAsync() {
        PageStorageOptions options = TestOptions.Storage with { MaxKeysPerPage = 2 };
        FakePageStateStore store = Live(options);
        IPageStorage storage = new PageStorageScope(Key("/page"), store, options, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(async () => {
            await storage.SetAsync("one", 1);
            await storage.SetAsync("two", 2);
            await storage.SetAsync("three", 3);
        });
    }

    [Test]
    public async Task AWriteToADeletedPageIsRefusedAsync() {
        FakePageStateStore store = new();
        IPageStorage storage = new PageStorageScope(Key("/gone"), store, TestOptions.Storage, writable: true);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SetAsync("key", 1));
    }

    [Test]
    public async Task AStoredValueOfTheWrongShapeThrowsRatherThanReportingAbsentAsync() {
        FakePageStateStore store = Live();
        store.Seed("/page", "entry", "\"text\"");
        IPageStorage storage = new PageStorageScope(Key("/page"), store, TestOptions.Storage, writable: true);

        await Assert.ThrowsAsync<JsonException>(() => storage.TryGetValueAsync<Entry>("entry"));
    }

    [Test]
    public async Task BadJsonThrowsRatherThanReportingAbsentAsync() {
        FakePageStateStore store = Live();
        store.Seed("/page", "entry", "{not json");
        IPageStorage storage = new PageStorageScope(Key("/page"), store, TestOptions.Storage, writable: true);

        await Assert.ThrowsAsync<JsonException>(() => storage.TryGetValueAsync<Entry>("entry"));
    }

    [Test]
    public async Task AHeadRenderMayReadButNotWriteAsync() {
        FakePageStateStore store = Live();
        store.Seed("/page", "visits", "1");
        PageStorageScope storage = new(Key("/page"), store, TestOptions.Storage, writable: false);

        Assert.That((await storage.TryGetValueAsync<int>("visits")).Value, Is.EqualTo(1));
        Assert.That(await storage.KeysAsync(), Is.Not.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("visits", 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync("visits"));
    }

    [Test]
    public async Task AnAbandonedRenderCannotUseStorageAsync() {
        PageStorageScope storage = new(Key("/page"), Live(), TestOptions.Storage, writable: true);

        storage.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("key", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.TryGetValueAsync<int>("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.KeysAsync());
    }

    [Test]
    public async Task TheRenderDeadlineIsPassedToTheStoreAsync() {
        FakePageStateStore store = Live();
        PageStorageScope storage = new(Key("/page"), store, TestOptions.Storage, writable: true);
        using CancellationTokenSource deadline = new();
        storage.AttachDeadline(deadline.Token);

        await storage.SetAsync("key", 1);

        Assert.That(store.LastCancellationToken, Is.EqualTo(deadline.Token));
    }

    [Test]
    public async Task AnAlreadyCancelledDeadlineRefusesTheCallAsync() {
        PageStorageScope storage = new(Key("/page"), Live(), TestOptions.Storage, writable: true);
        using CancellationTokenSource deadline = new();
        deadline.Cancel();
        storage.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => storage.SetAsync("key", 1));
    }

    [Test]
    public async Task TheLastWriteWinsAsync() {
        IPageStorage storage = Scope();

        await storage.SetAsync("key", "first");
        await storage.SetAsync("key", "second");

        Assert.That((await storage.TryGetValueAsync<string>("key")).Value, Is.EqualTo("second"));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private static PageStorageScope Scope() => new(Key("/page"), Live(), TestOptions.Storage, writable: true);

    private static FakePageStateStore Live(PageStorageOptions? options = null) {
        FakePageStateStore store = new(options);
        store.LivePages.Add("/page");

        return store;
    }

    private sealed record Entry(string Name, int Count);
}
