// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Reflection;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the site's own facts are one closed fact sheet.</summary>
[TestFixture]
public sealed class SiteInfoTests {

    private static readonly string[] ExpectedMembers = [
        "Bounds",
        "Commits",
        "Contact",
        "DeployCommit",
        "Documents",
        "EditorPath",
        "Name",
        "Origin",
        "Reference",
        "RepositoryUrl",
    ];

    [Test]
    public void TheContractIsAClosedSetOfProperties() {
        PropertyInfo[] properties = [.. typeof(ISiteInfo).GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)];

        Assert.Multiple(() => {
            Assert.That(properties.Select(property => property.Name), Is.EqualTo(ExpectedMembers),
                "a new fact is a deliberate edit to this list");
            Assert.That(typeof(ISiteInfo).GetMethods().Where(method => !method.IsSpecialName), Is.Empty,
                "there is no verb, and no key lookup by name");
            Assert.That(typeof(ISiteInfo).GetEvents(), Is.Empty);
            Assert.That(properties, Has.All.Matches<PropertyInfo>(property =>
                property is not null && property.CanRead && !property.CanWrite
                && property.GetIndexParameters().Length == 0),
                "every fact is a readable property, not an indexer");
        });
    }

    [Test]
    public void TheIdentityIsTheSitesOwn() {
        SiteInfo info = Info(Facts());

        Assert.Multiple(() => {
            Assert.That(info.Origin, Is.EqualTo(TestOptions.Site.Origin));
            Assert.That(info.EditorPath, Is.EqualTo(TestOptions.Editor.Key));
            Assert.That(info.DeployCommit, Is.EqualTo(BuildInfo.Current.Commit));
            Assert.That(info.RepositoryUrl, Is.EqualTo(SiteIdentity.RepositoryUrl));
            Assert.That(info.Name, Is.EqualTo(SiteIdentity.Name));
            Assert.That(info.Contact, Is.EqualTo(SiteIdentity.Contact));
        });
    }

    [Test]
    public void ABuildWithoutACommitAnswersNull() {
        SiteFacts facts = new(TestOptions.Site, TestOptions.Editor, "   ", () => [], () => [], () => []);

        Assert.That(Info(facts).DeployCommit, Is.Null, "blank is absent, not a commit called spaces");
    }

    [Test]
    public void EachSourceIsReadOnceAndHeld() {
        int documents = 0;
        int commits = 0;
        int reference = 0;
        SiteDocument document = new(SiteDocumentKind.SiteLicence, "LICENSE", SiteDocumentFormat.Text, "MIT");
        SiteFacts facts = new(
            TestOptions.Site,
            TestOptions.Editor,
            "abc",
            () => { documents++; return [document]; },
            () => { commits++; return [new SiteCommit("abc", "First")]; },
            () => { reference++; return []; });
        SiteInfo info = Info(facts);

        IReadOnlyList<SiteDocument> first = info.Documents;
        IReadOnlyList<SiteDocument> second = info.Documents;
        _ = info.Commits;
        _ = info.Reference;

        Assert.Multiple(() => {
            Assert.That(first, Is.SameAs(second), "the read is held for the life of the process");
            Assert.That(documents, Is.EqualTo(1));
            Assert.That(commits, Is.EqualTo(1));
            Assert.That(reference, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheWarmUpAndTheFirstCallerShareOneReadAsync() {
        int documents = 0;
        int commits = 0;
        int reference = 0;
        SiteFacts facts = new(
            TestOptions.Site,
            TestOptions.Editor,
            null,
            () => { Interlocked.Increment(ref documents); return []; },
            () => { Interlocked.Increment(ref commits); return []; },
            () => { Interlocked.Increment(ref reference); return []; });

        await Task.WhenAll(facts.WarmAsync(), Task.Run(() => _ = facts.Documents));

        Assert.Multiple(() => {
            Assert.That(documents, Is.EqualTo(1), "the warm-up and the caller read the same source once");
            Assert.That(commits, Is.EqualTo(1));
            Assert.That(reference, Is.EqualTo(1));
        });
    }

    [Test]
    public void TheBoundsAreAskedAgainEachTimeTheyAreRead() {
        int asked = 0;
        SiteInfo info = new(Facts(), () => {
            asked++;
            return Bounds(asked);
        });

        _ = info.Bounds;
        _ = info.Bounds;

        Assert.That(asked, Is.EqualTo(2), "the bounds are a snapshot, not a fact held for the process");
    }

    internal static SiteFacts Facts(
        IReadOnlyList<SiteDocument>? documents = null,
        IReadOnlyList<SiteCommit>? commits = null,
        IReadOnlyList<ReferenceCorpus>? reference = null) => new(
            TestOptions.Site,
            TestOptions.Editor,
            BuildInfo.Current.Commit,
            () => documents ?? [],
            () => commits ?? [],
            () => reference ?? []);

    internal static SiteInfo Info(SiteFacts facts) => new(facts, () => Bounds(0));

    internal static SiteBounds Bounds(int available) => new(
        new SiteAllowance(10, available, 2, "Pages:Fetch:FetchesPerSecond"),
        new SiteAllowance(100, available * 10, 20, "Pages:Read:SourceBytesPerSecond"),
        new SiteCounter(1000, available * 10, "Pages:Render:MaxBodyBytes"),
        new SiteCounter(8, available, "Pages:Render:MaxEmbeds"),
        new SiteTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(available), "Pages:Render:TimeoutSeconds"));
}
