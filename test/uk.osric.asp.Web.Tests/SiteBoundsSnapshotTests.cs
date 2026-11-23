// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the bounds are read from the objects that enforce them.</summary>
[TestFixture]
public sealed class SiteBoundsSnapshotTests {

    [Test]
    public void ARendersBoundsAreTheBudgetsOwnFigures() {
        FetchBudget fetches = new(10, 2, TimeProvider.System, PageFetchOptions.RateKey);
        ReadBudget template = new(524_290, 104_858, TimeProvider.System, PageReadOptions.RateKey);
        BodyBudget body = new(4096);
        _ = body.TryCharge(1024);

        SiteBounds bounds = SiteBoundsSnapshot.ForRender(
            fetches, template, body, embedCapacity: 32, embedsMade: 2,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));

        Assert.Multiple(() => {
            Assert.That(bounds.Fetches.Available, Is.EqualTo(fetches.Available));
            Assert.That(bounds.Fetches.Capacity, Is.EqualTo(10));
            Assert.That(bounds.Fetches.PerSecond, Is.EqualTo(2));
            Assert.That(bounds.Fetches.Key, Is.EqualTo("Pages:Fetch:FetchesPerSecond"));
            Assert.That(bounds.Template.Available, Is.EqualTo(template.Available));
            Assert.That(bounds.Template.Key, Is.EqualTo("Pages:Read:SourceCharsPerSecond"));
            Assert.That(bounds.Body, Is.EqualTo(new SiteCounter(4096, 3072, "Pages:Render:MaxBodyChars")));
            Assert.That(bounds.Embeds, Is.EqualTo(new SiteCounter(32, 30, "Pages:Render:MaxEmbeds")));
            Assert.That(bounds.Time, Is.EqualTo(new SiteTime(
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), "Pages:Render:TimeoutSeconds")));
        });
    }

    [Test]
    public void AFetchSpentMovesTheFigure() {
        FetchBudget fetches = new(10, 2, TimeProvider.System, PageFetchOptions.RateKey);
        ReadBudget template = new(524_290, 104_858, TimeProvider.System, PageReadOptions.RateKey);
        BodyBudget body = new(4096);

        SiteBounds before = SiteBoundsSnapshot.ForRender(
            fetches, template, body, 32, 0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _ = fetches.TryTake();
        SiteBounds after = SiteBoundsSnapshot.ForRender(
            fetches, template, body, 32, 0, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        Assert.That(after.Fetches.Available, Is.EqualTo(before.Fetches.Available - 1));
    }

    [Test]
    public void MoreEmbedsThanTheCapacityRemainAtZero() {
        SiteBounds bounds = SiteBoundsSnapshot.ForRender(
            new FetchBudget(10, 2, TimeProvider.System, PageFetchOptions.RateKey),
            new ReadBudget(524_290, 104_858, TimeProvider.System, PageReadOptions.RateKey),
            new BodyBudget(4096),
            embedCapacity: 4,
            embedsMade: 9,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));

        Assert.That(bounds.Embeds!.Remaining, Is.Zero);
    }

    [Test]
    public void AJobsBoundsAreItsOwnRatesAndCeiling() {
        SiteBounds bounds = SiteBoundsSnapshot.ForJob(
            new FetchBudget(1440, 2, TimeProvider.System, PageJobOptions.FetchRateKey),
            new ReadBudget(1_048_580, 104_858, TimeProvider.System, PageJobOptions.ReadRateKey),
            TimeSpan.FromSeconds(32_400), TimeSpan.FromSeconds(100));

        Assert.Multiple(() => {
            Assert.That(bounds.Fetches.Key, Is.EqualTo("Pages:Jobs:FetchesPerSecond"));
            Assert.That(bounds.Template.Key, Is.EqualTo("Pages:Jobs:SourceCharsPerSecond"));
            Assert.That(bounds.Time, Is.EqualTo(new SiteTime(
                TimeSpan.FromSeconds(32_400), TimeSpan.FromSeconds(100), "Pages:Jobs:MaxDurationSeconds")));
            Assert.That(bounds.Body, Is.Null, "a job has no response to buffer");
            Assert.That(bounds.Embeds, Is.Null, "a job cannot compose a page");
        });
    }
}
