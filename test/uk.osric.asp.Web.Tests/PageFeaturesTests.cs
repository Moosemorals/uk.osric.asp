// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageFeaturesTests {

    [Test]
    public void TryGetReportsARegisteredCapability() {
        TestCapability capability = new();
        PageFeatures features = new PageFeatures().Add<TestCapability>(capability);

        bool found = features.TryGet<TestCapability>(out TestCapability? feature);

        Assert.That(found, Is.True);
        Assert.That(feature, Is.SameAs(capability));
    }

    [Test]
    public void TryGetReportsAMissingCapabilityAndLeavesTheDefault() {
        PageFeatures features = new();

        bool found = features.TryGet<IPageStorage>(out IPageStorage? feature);

        Assert.That(found, Is.False);
        Assert.That(feature, Is.Null);
    }

    [Test]
    public void AnInterfaceLookupFindsACapabilityRegisteredByItsImplementation() {
        TestStorage storage = new();
        PageFeatures features = new PageFeatures().Add(storage);

        bool found = features.TryGet<IPageStorage>(out IPageStorage? feature);

        Assert.That(found, Is.True);
        Assert.That(feature, Is.SameAs(storage));
    }

    [Test]
    public void GetReturnsARegisteredCapability() {
        TestCapability capability = new();
        IPageFeatures features = new PageFeatures().Add<TestCapability>(capability);

        Assert.That(features.Get<TestCapability>(), Is.SameAs(capability));
    }

    [Test]
    public void GetReturnsNullForAMissingCapability() {
        IPageFeatures features = new PageFeatures();

        Assert.That(features.Get<IPageStorage>(), Is.Null);
    }

    [Test]
    public void GetRequiredReturnsARegisteredCapability() {
        TestCapability capability = new();
        IPageFeatures features = new PageFeatures().Add<TestCapability>(capability);

        Assert.That(features.GetRequired<TestCapability>(), Is.SameAs(capability));
    }

    [Test]
    public void GetRequiredNamesTheMissingInterface() {
        IPageFeatures features = new PageFeatures();

        InvalidOperationException? refused = Assert.Throws<InvalidOperationException>(
            () => features.GetRequired<IHttpFetch>());

        Assert.That(refused?.Message, Does.Contain(nameof(IHttpFetch)));
    }

    [Test]
    public void ARenderNeverRegistersANullCapability() {
        PageFeatures features = new();

        Assert.That(() => features.Add<IPageStorage>(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void ACapabilityIsRegisteredOnce() {
        PageFeatures features = new PageFeatures().Add<TestCapability>(new TestCapability());

        Assert.That(
            () => features.Add<TestCapability>(new TestCapability()),
            Throws.InvalidOperationException);
    }

    [Test]
    public void AttachDeadlineReachesABoundCapability() {
        TestCapability capability = new();
        PageFeatures features = new PageFeatures().Add<TestCapability>(capability);
        using CancellationTokenSource deadline = new();

        features.AttachDeadline(deadline.Token);

        Assert.That(capability.Deadline, Is.EqualTo(deadline.Token));
    }

    [Test]
    public void AbandonReachesABoundCapability() {
        TestCapability capability = new();
        PageFeatures features = new PageFeatures().Add<TestCapability>(capability);

        features.Abandon();

        Assert.That(capability.Abandoned, Is.True);
    }

    private sealed class TestStorage : IPageStorage {

        public Task<PageValue<T>> TryGetValueAsync<T>(string key) =>
            Task.FromResult(new PageValue<T>(false, default));

        public Task SetAsync<T>(string key, T value) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string key) => Task.FromResult(false);

        public Task<IReadOnlyList<string>> KeysAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class TestCapability : IRenderBoundFeature {

        public CancellationToken Deadline { get; private set; }

        public bool Abandoned { get; private set; }

        public void AttachDeadline(CancellationToken deadline) => Deadline = deadline;

        public void Abandon() => Abandoned = true;
    }
}
