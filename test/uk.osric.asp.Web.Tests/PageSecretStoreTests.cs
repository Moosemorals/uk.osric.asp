// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary><c>ctx.Stores.Page.Secrets</c>: one layer's own key, and the verbs around it.</summary>
/// <remarks>
/// The visitor subject has no secret shape, so there is no reader here to slide or to resolve: a
/// secret is the page's. The layout consequence is the same rule seen once: two handles bound to two
/// keys share a store and see only their own rows.
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class PageSecretStoreTests {

    [Test]
    public async Task AWriteLandsUnderTheLayersOwnKeyAsync() {
        FakeStoreSecrets secrets = new();
        await Handle("/a", secrets).SetAsync("signing", "a key");

        Assert.Multiple(() => {
            Assert.That(secrets.Rows, Has.Count.EqualTo(1));
            Assert.That(secrets.Rows[0].Subject, Is.EqualTo("page:/a"));
            Assert.That(secrets.Rows[0].PageKey, Is.EqualTo("/a"));
        });
    }

    [Test]
    public async Task ASecretRoundTripsAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle("/a", secrets);

        await handle.SetAsync("signing", "a key");

        Assert.That(await handle.TryGetAsync("signing"), Is.EqualTo("a key"));
    }

    [Test]
    public async Task AMissingNameAnswersNothingAsync() {
        Assert.That(await Handle("/a", new FakeStoreSecrets()).TryGetAsync("signing"), Is.Null);
    }

    [Test]
    public async Task AnotherLayersHandleCannotReadTheSecretAsync() {
        FakeStoreSecrets secrets = new();
        await Handle("/a", secrets).SetAsync("signing", "a key");

        await Assert.MultipleAsync(async () => {
            Assert.That(await Handle("/_layout", secrets).TryGetAsync("signing"), Is.Null);
            Assert.That(await Handle("/_layout", secrets).NamesAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task NamesAnswerNamesAndNeverAValueAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle("/a", secrets);
        await handle.SetAsync("zeta", "one");
        await handle.SetAsync("alpha", "two");

        IReadOnlyList<string> names = await handle.NamesAsync();

        await Assert.MultipleAsync(async () => {
            Assert.That(names, Is.EqualTo(new List<string> { "alpha", "zeta" }));
            // A listing consumes nothing, so the second answer is the same one.
            Assert.That(await handle.NamesAsync(), Is.EqualTo(names));
            Assert.That(await handle.TryGetAsync("alpha"), Is.EqualTo("two"));
        });
    }

    [Test]
    public async Task ADeletionAnswersWhetherSomethingWasRemovedAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle("/a", secrets);
        await handle.SetAsync("signing", "a key");

        await Assert.MultipleAsync(async () => {
            Assert.That(await handle.DeleteAsync("signing"), Is.True);
            Assert.That(await handle.DeleteAsync("signing"), Is.False);
            Assert.That(await handle.TryGetAsync("signing"), Is.Null);
        });
    }

    [Test]
    public async Task AHeadReadsASecretAndRefusesEveryWriteAsync() {
        FakeStoreSecrets secrets = new();
        await Handle("/a", secrets).SetAsync("signing", "a key");
        PageSecretStore head = Handle("/a", secrets, writable: false);

        await Assert.MultipleAsync(async () => {
            Assert.That(await head.TryGetAsync("signing"), Is.EqualTo("a key"));
            Assert.That(await head.NamesAsync(), Is.EqualTo(new List<string> { "signing" }));

            InvalidOperationException stored = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await head.SetAsync("other", "value")))!;
            InvalidOperationException deleted = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await head.DeleteAsync("signing")))!;

            Assert.That(stored.Message, Does.Contain("may not write a secret during a HEAD request"));
            Assert.That(deleted.Message, Does.Contain("may not write a secret during a HEAD request"));
        });
    }

    [Test]
    public async Task AnAbandonedRenderRefusesEveryCallAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle("/a", secrets);
        handle.Abandon();

        await Assert.MultipleAsync(async () => {
            await Assert.ThatAsync(
                async () => await handle.TryGetAsync("signing"),
                Throws.InvalidOperationException.With.Message.Contains("was abandoned"));
            await Assert.ThatAsync(
                async () => await handle.SetAsync("signing", "a key"),
                Throws.InvalidOperationException.With.Message.Contains("was abandoned"));
            await Assert.ThatAsync(
                async () => await handle.DeleteAsync("signing"),
                Throws.InvalidOperationException.With.Message.Contains("was abandoned"));
            await Assert.ThatAsync(
                async () => await handle.NamesAsync(),
                Throws.InvalidOperationException.With.Message.Contains("was abandoned"));
        });
    }

    [Test]
    public async Task APassedDeadlineEndsTheCallAsync() {
        PageSecretStore handle = Handle("/a", new FakeStoreSecrets());
        handle.AttachDeadline(new CancellationToken(canceled: true));

        await Assert.ThatAsync(
            async () => await handle.TryGetAsync("signing"),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task AnEmptyValueIsRefusedAsync() {
        PageSecretStore handle = Handle("/a", new FakeStoreSecrets());

        await Assert.MultipleAsync(async () => {
            ArgumentException empty = (await Assert.ThrowsAsync<ArgumentException>(
                async () => await handle.SetAsync("signing", "")))!;
            ArgumentException name = (await Assert.ThrowsAsync<ArgumentException>(
                async () => await handle.SetAsync("", "a key")))!;

            Assert.That(empty.Message, Does.Contain("value"));
            Assert.That(name.Message, Does.Contain("name"));
        });
    }

    [Test]
    public async Task ANameOverItsLengthIsRefusedAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle("/a", secrets, limits: TestOptions.Stores.Secrets with { MaxNameChars = 8 });

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await handle.SetAsync("a-name-too-long", "a key")))!;

        Assert.Multiple(() => {
            Assert.That(refused.Message, Does.Contain("at most 8 characters"));
            Assert.That(secrets.WriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task AValueOverItsLimitIsRefusedNamingTheSettingAsync() {
        FakeStoreSecrets secrets = new();
        PageSecretStore handle = Handle(
            "/a", secrets, limits: TestOptions.Stores.Secrets with { MaxSecretBytes = 64 });

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await handle.SetAsync("signing", new string('x', 65))))!;

        Assert.Multiple(() => {
            Assert.That(refused.Message, Does.Contain("Pages:Stores:Secrets:MaxSecretBytes is 64"));
            Assert.That(secrets.WriteCount, Is.Zero);
        });
    }

    private static PageSecretStore Handle(
        string key,
        FakeStoreSecrets secrets,
        bool writable = true,
        PageSecretOptions? limits = null) =>
        new(PagePath.Normalize(key), secrets, limits ?? TestOptions.Stores.Secrets, writable);
}
