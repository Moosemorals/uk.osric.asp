// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>The startup step that writes the seed a release moved, proven against the real host.</summary>
/// <remarks>
/// Every start is its own host on one database, which is what a deploy is: the point of the step is
/// that it happens in the startup path, so a unit test over the applier alone would not prove it.
/// </remarks>
[TestFixture]
public sealed class SeedAutoApplyTests {

    private const string Key = "/about";
    private const string Older = "<h1>the seed /about came from</h1>";

    private string _database = string.Empty;

    [SetUp]
    public void NewDatabase() {
        _database = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.seed-auto-apply.{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void DeleteDatabase() {
        foreach (string suffix in new[] { "", "-shm", "-wal" }) {
            File.Delete(_database + suffix);
        }
    }

    [Test]
    public async Task AStartAppliesTheReleaseNewerSeedAndTheNextStartWritesNothingAsync() {
        string shipped;
        int held;
        using (AspApplication first = Start()) {
            _ = first.CreateClient();
            shipped = (await PageAsync(first))!.Template;
            await HoldTheOlderSeedAsync(first);
            held = (await RevisionsAsync(first)).Count;
        }

        int applied;
        string? logged;
        using (AspApplication second = Start()) {
            _ = second.CreateClient();
            applied = (await RevisionsAsync(second)).Count;
            logged = second.Logs.Entries
                .Select(entry => entry.Message)
                .FirstOrDefault(message => message.Contains("release's seed of " + Key, StringComparison.Ordinal));
            string? content = (await PageAsync(second))?.Template;
            string? anchor = (await HeadAsync(second))?.SeedHash;
            Assert.Multiple(() => {
                Assert.That(content, Is.EqualTo(shipped), "the store holds the release's seed");
                Assert.That(anchor, Is.EqualTo(TemplateFingerprint.Of(shipped)));
            });
        }

        using (AspApplication third = Start()) {
            _ = third.CreateClient();
            int restarted = (await RevisionsAsync(third)).Count;
            Assert.Multiple(() => {
                Assert.That(held, Is.EqualTo(2), "the older seed was stored as its own revision");
                Assert.That(applied, Is.EqualTo(3), "the start wrote the release's seed");
                Assert.That(restarted, Is.EqualTo(applied), "nothing is left to write on the next start");
                Assert.That(logged, Is.Not.Null, "the write is reported");
            });
        }
    }

    private AspApplication Start() {
        return new AspApplication(settings: new Dictionary<string, string?> {
            ["ConnectionStrings:Storage"] = $"Data Source={_database}",
        });
    }

    private static async Task<PageTemplate?> PageAsync(AspApplication application) {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPageStore>()
            .GetPageAsync(PagePath.Normalize(Key));
    }

    private static async Task<PageHeadSummary?> HeadAsync(AspApplication application) {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPageStore>()
            .GetHeadAsync(PagePath.Normalize(Key));
    }

    private static async Task<IReadOnlyList<PageRevisionSummary>> RevisionsAsync(AspApplication application) {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPageStore>()
            .ListRevisionsAsync(PagePath.Normalize(Key));
    }

    // The row a release moved: the page holds the seed it was installed from, and this release ships
    // a different one.
    private static async Task HoldTheOlderSeedAsync(AspApplication application) {
        await using AsyncServiceScope scope = application.Services.CreateAsyncScope();
        PagePath path = PagePath.Normalize(Key);
        await scope.ServiceProvider.GetRequiredService<IPageStore>().SavePageAsync(new PageTemplate(path, Older) {
            Kind = TemplateSourceGenerator.Generate(Older).Kind,
            SeedHash = TemplateFingerprint.Of(Older),
        });
    }
}
