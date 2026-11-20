// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What a start does with the seeds a release moved.</summary>
/// <remarks>
/// The pack each test reads is a folder it writes, so one row is arranged without the rest of the
/// shipped pack taking part. The write goes through the same saver a page's install uses, so a test
/// sees exactly what the host would store.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageSeedApplierTests {

    private const string Shipped = "<h1>the release's /about</h1>";
    private const string Older = "<h1>the seed it was installed from</h1>";

    private readonly FakePageStore _storage = new();
    private PageHarness? _harness;
    private string? _folder;

    [TearDown]
    public void DeletePack() {
        _harness?.Dispose();
        _harness = null;

        if (_folder is not null && Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        _folder = null;
    }

    [Test]
    public async Task AReleaseNewerRowIsWrittenAndAnchoredAsync() {
        PageSeeds seeds = Pack(("/about", Shipped));
        Hold("/about", Older, Older);

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        PageHeadSummary? head = await HeadAsync("/about");
        int revisions = (await RevisionsAsync("/about")).Count;
        PageSeedInstaller.SeedOutcome[] written = [PageSeedInstaller.SeedOutcome.Written];
        Assert.Multiple(() => {
            Assert.That(_storage["/about"]?.Template, Is.EqualTo(Shipped));
            Assert.That(head?.SeedHash, Is.EqualTo(TemplateFingerprint.Of(Shipped)));
            Assert.That(revisions, Is.EqualTo(2), "the source it replaced stays in history");
            Assert.That(results.Select(result => result.Outcome), Is.EqualTo(written));
        });
    }

    // A new seed is the startup seeder's, and each of the other six verdicts needs something the
    // owner chose, so this step writes none of them.
    private static IEnumerable<TestCaseData> OtherVerdicts() {
        yield return new TestCaseData("in sync");
        yield return new TestCaseData("edited");
        yield return new TestCaseData("conflict");
        yield return new TestCaseData("store newer");
        yield return new TestCaseData("unknown");
        yield return new TestCaseData("deleted");
    }

    [TestCaseSource(nameof(OtherVerdicts))]
    public async Task EveryOtherVerdictIsLeftAloneAsync(string verdict) {
        PageSeeds seeds = Pack(("/about", Shipped));
        Arrange(verdict);

        IReadOnlyList<PageSeedInstaller.SeedPlan> plan = await PageSeedInstaller.PlanAsync(Editor(), seeds, CancellationToken.None);
        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);
        PageHeadSummary? head = await HeadAsync("/about");
        int revisions = (await RevisionsAsync("/about")).Count;

        Assert.Multiple(() => {
            Assert.That(plan.Single().Summary.Verdict, Is.EqualTo(verdict), "the row was arranged as this verdict");
            Assert.That(plan.Single().Summary.CanApply, Is.False);
            Assert.That(results, Is.Empty, "nothing is a candidate but a release-newer row");
            Assert.That(_storage["/about"]?.Template, Is.EqualTo(verdict is "edited" or "conflict" or "unknown" ? "<h1>mine</h1>" : Shipped));
            Assert.That(head?.SeedHash, Is.EqualTo(ExpectedAnchor(verdict)));
            Assert.That(revisions, Is.EqualTo(1), "no revision was written");
        });
    }

    // The create-seeder runs immediately before this step, so a key with no head is not this step's
    // to write: creating is a different rule and a different write.
    [Test]
    public async Task ASeedTheStoreIsMissingIsNotCreatedAsync() {
        PageSeeds seeds = Pack(("/about", Shipped));

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        PageHeadSummary? head = await HeadAsync("/about");
        Assert.Multiple(() => {
            Assert.That(results, Is.Empty);
            Assert.That(head, Is.Null, "the create-seeder owns a key the store has nothing at");
        });
    }

    // Adopting writes no source, so the only thing it changes is a claim about where the page came
    // from — and a page that matches by coincidence would then be overwritten by the next release.
    [Test]
    public async Task AnAdoptableRowIsNotRecordedAsInstalledAsync() {
        PageSeeds seeds = Pack(("/about", Shipped));
        Hold("/about", Shipped, null);

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        PageHeadSummary? head = await HeadAsync("/about");
        Assert.Multiple(() => {
            Assert.That(results, Is.Empty);
            Assert.That(head?.SeedHash, Is.Null, "the anchor is still the page's to record");
        });
    }

    [Test]
    public async Task ASecondStartWritesNothingAsync() {
        PageSeeds seeds = Pack(("/about", Shipped));
        Hold("/about", Older, Older);
        _ = await ApplyAsync(seeds);
        int revisions = (await RevisionsAsync("/about")).Count;

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        int after = (await RevisionsAsync("/about")).Count;
        Assert.Multiple(() => {
            Assert.That(results, Is.Empty, "the row is in sync now, so nothing is a candidate");
            Assert.That(after, Is.EqualTo(revisions));
        });
    }

    // The rule, not an exception to it: a page put back on the source it came from is release newer
    // again, and the start writes it again.
    [Test]
    public async Task APagePutBackOnItsOldSeedIsWrittenOnTheNextStartAsync() {
        PageSeeds seeds = Pack(("/about", Shipped));
        Hold("/about", Older, Older);
        _ = await ApplyAsync(seeds);

        Hold("/about", Older, Older);

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        PageSeedInstaller.SeedOutcome[] written = [PageSeedInstaller.SeedOutcome.Written];
        Assert.Multiple(() => {
            Assert.That(_storage["/about"]?.Template, Is.EqualTo(Shipped));
            Assert.That(results.Select(result => result.Outcome), Is.EqualTo(written));
        });
    }

    [Test]
    public async Task ARowThatFailsDoesNotStopTheNextAsync() {
        PageSeeds seeds = Pack(("/about", Shipped), ("/contact", Shipped));
        Hold("/about", Older, Older);
        Hold("/contact", Older, Older);
        _storage.FailOnWrite = path => string.Equals(path.Key, "/about", StringComparison.Ordinal)
            ? new IOException("the disk said no")
            : null;

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        (string Key, PageSeedInstaller.SeedOutcome Outcome)[] expected = [
            ("/about", PageSeedInstaller.SeedOutcome.Failed),
            ("/contact", PageSeedInstaller.SeedOutcome.Written),
        ];
        Assert.Multiple(() => {
            Assert.That(_storage["/about"]?.Template, Is.EqualTo(Older), "the failed row is left as it was");
            Assert.That(_storage["/contact"]?.Template, Is.EqualTo(Shipped), "the row after it still ran");
            Assert.That(results.Select(result => (result.Path.Key, result.Outcome)), Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task ASeedThatDoesNotCompileIsWrittenAndReportedAsync() {
        const string Broken = "<% var x = ; %>";
        PageSeeds seeds = Pack(("/about", Broken));
        Hold("/about", Older, Older);

        IReadOnlyList<PageSeedInstaller.SeedResult> results = await ApplyAsync(seeds);

        PageSeedInstaller.SeedOutcome[] expected = [PageSeedInstaller.SeedOutcome.WrittenWithErrors];
        Assert.Multiple(() => {
            Assert.That(_storage["/about"]?.Template, Is.EqualTo(Broken), "a broken seed is stored, so the row does not stay pending");
            Assert.That(results.Select(result => result.Outcome), Is.EqualTo(expected));
        });
    }

    private void Arrange(string verdict) {
        switch (verdict) {
            case "in sync":
                Hold("/about", Shipped, Shipped);
                break;
            case "edited":
                Hold("/about", "<h1>mine</h1>", Shipped);
                break;
            case "conflict":
                Hold("/about", "<h1>mine</h1>", Older);
                break;
            case "store newer":
                Hold("/about", Shipped, Older);
                break;
            case "unknown":
                Hold("/about", "<h1>mine</h1>", null);
                break;
            case "deleted":
                Hold("/about", Shipped, Shipped);
                Assert.That(_storage.DeletePageAsync(PagePath.Normalize("/about")).Result, Is.True, "the row was arranged as a tombstone");
                break;
            default:
                Assert.Fail($"{verdict} is not a verdict this test arranges");
                break;
        }
    }

    private async Task<IReadOnlyList<PageSeedInstaller.SeedResult>> ApplyAsync(PageSeeds seeds) {
        return await PageSeedApplier.ApplyAsync(Editor(), seeds, NullLogger.Instance, CancellationToken.None);
    }

    private PageEditor Editor() => (_harness ??= new PageHarness(_storage)).Editor;

    private Task<PageHeadSummary?> HeadAsync(string path) => _storage.GetHeadAsync(PagePath.Normalize(path));

    private Task<IReadOnlyList<PageRevisionSummary>> RevisionsAsync(string path) =>
        _storage.ListRevisionsAsync(PagePath.Normalize(path));

    private static string? ExpectedAnchor(string verdict) => verdict switch {
        "in sync" or "edited" or "deleted" => TemplateFingerprint.Of(Shipped),
        "conflict" or "store newer" => TemplateFingerprint.Of(Older),
        _ => null,
    };

    // A page holding this source, anchored to the seed it was installed from, or to nothing.
    private void Hold(string path, string source, string? seedItCameFrom) {
        PagePath key = PagePath.Normalize(path);
        _storage[key.Key] = new PageTemplate(key, source) {
            Kind = TemplateSourceGenerator.Generate(source).Kind,
            SeedHash = seedItCameFrom is null ? null : TemplateFingerprint.Of(seedItCameFrom),
        };
    }

    private PageSeeds Pack(params (string Path, string Source)[] seeds) {
        _folder = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.seed-apply.{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_folder);

        foreach ((string path, string source) in seeds) {
            string file = Path.Combine(_folder, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, source);
        }

        return new PageSeeds(_folder);
    }
}
