// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageCapabilitiesTests {

    [Test]
    public void TryGetReportsARegisteredCapability() {
        TestCapability capability = new();
        PageCapabilities capabilities = new PageCapabilities().Add(capability);

        bool found = capabilities.TryGet(out TestCapability? foundCapability);

        Assert.That(found, Is.True);
        Assert.That(foundCapability, Is.SameAs(capability));
    }

    [Test]
    public void TryGetReportsAMissingCapabilityAndLeavesTheDefault() {
        PageCapabilities capabilities = new();

        bool found = capabilities.TryGet(out IPageStorage? foundCapability);

        Assert.That(found, Is.False);
        Assert.That(foundCapability, Is.Null);
    }

    [Test]
    public void AnInterfaceLookupFindsACapabilityRegisteredByItsImplementation() {
        TestStorage storage = new();
        PageCapabilities capabilities = new PageCapabilities().Add(storage);

        bool found = capabilities.TryGet(out IPageStorage? foundCapability);

        Assert.That(found, Is.True);
        Assert.That(foundCapability, Is.SameAs(storage));
    }

    [Test]
    public void GetReturnsARegisteredCapability() {
        TestCapability capability = new();
        IPageCapabilities capabilities = new PageCapabilities().Add(capability);

        Assert.That(capabilities.Get<TestCapability>(), Is.SameAs(capability));
    }

    [Test]
    public void GetReturnsNullForAMissingCapability() {
        IPageCapabilities capabilities = new PageCapabilities();

        Assert.That(capabilities.Get<IPageStorage>(), Is.Null);
    }

    [Test]
    public void GetRequiredReturnsARegisteredCapability() {
        TestCapability capability = new();
        IPageCapabilities capabilities = new PageCapabilities().Add(capability);

        Assert.That(capabilities.GetRequired<TestCapability>(), Is.SameAs(capability));
    }

    [Test]
    public void GetRequiredNamesTheMissingInterface() {
        IPageCapabilities capabilities = new PageCapabilities();

        InvalidOperationException? refused = Assert.Throws<InvalidOperationException>(
            () => capabilities.GetRequired<IHttpFetch>());

        Assert.That(refused?.Message, Does.Contain(nameof(IHttpFetch)));
    }

    [Test]
    public void ARenderNeverRegistersANullCapability() {
        PageCapabilities capabilities = new();

        Assert.That(() => capabilities.Add<IPageStorage>(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void ACapabilityIsRegisteredOnce() {
        PageCapabilities capabilities = new PageCapabilities().Add(new TestCapability());

        Assert.That(
            () => capabilities.Add(new TestCapability()),
            Throws.InvalidOperationException);
    }

    [Test]
    public void AttachDeadlineReachesABoundCapability() {
        TestCapability capability = new();
        PageCapabilities capabilities = new PageCapabilities().Add(capability);
        using CancellationTokenSource deadline = new();

        capabilities.AttachDeadline(deadline.Token);

        Assert.That(capability.Deadline, Is.EqualTo(deadline.Token));
    }

    [Test]
    public void AbandonReachesABoundCapability() {
        TestCapability capability = new();
        PageCapabilities capabilities = new PageCapabilities().Add(capability);

        capabilities.Abandon();

        Assert.That(capability.Abandoned, Is.True);
    }

    private sealed class TestStorage : IPageStorage {

        public Task<PageValue<T>> TryGetValueAsync<T>(string key) =>
            Task.FromResult(new PageValue<T>(false, default));

        public Task SetAsync<T>(string key, T value) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string key) => Task.FromResult(false);

        public Task<IReadOnlyList<string>> KeysAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class TestCapability : IRenderBoundCapability {

        public CancellationToken Deadline { get; private set; }

        public bool Abandoned { get; private set; }

        public void AttachDeadline(CancellationToken deadline) => Deadline = deadline;

        public void Abandon() => Abandoned = true;
    }
}
