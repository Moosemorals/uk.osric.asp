// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ApplySeedsEndpointTests {

    [Test]
    public async Task ApplyWritesWhatTheReleaseMovedAndStopsAtTheFirstDriftAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        SeedFile admin = TestOptions.Seeds.Find(Key("/admin.css"))!;
        SeedFile help = TestOptions.Seeds.Find(Key("/help"))!;
        SeedFile privacy = TestOptions.Seeds.Find(Key("/privacy"))!;

        storage["/about"] = new PageTemplate(Key("/about"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), admin.Source) { SeedHash = TemplateFingerprint.Of("other") };
        storage["/help"] = new PageTemplate(Key("/help"), "mine") { SeedHash = help.Hash };
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "old") { SeedHash = TemplateFingerprint.Of("old") };

        string body = await ApplyAsync(pages);
        PageHeadSummary? recorded = await storage.GetHeadAsync(Key("/admin.css"));

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo(about.Source), "a release-newer row is written");
            Assert.That(storage["/about"]?.SeedHash, Is.EqualTo(about.Hash));
            Assert.That(recorded?.SeedHash, Is.EqualTo(admin.Hash), "a store-newer row is recorded");
            Assert.That(storage["/help"]?.Template, Is.EqualTo("mine"), "a drifted row is not written");
            Assert.That(storage["/privacy"]?.Template, Is.EqualTo("old"), "nothing after the drift is written");
            Assert.That(body, Does.Contain("stopped"));
        });
    }

    [Test]
    public async Task PreviewOffersNothingWhenEverySeedMatchesAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.SeedAsync();

        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("<form"));
            Assert.That(body, Does.Contain("Nothing needs applying"));
        });
    }

    [Test]
    public async Task PreviewNamesTheRowTheRunWouldStopAtAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "mine") { SeedHash = about.Hash };

        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("would stop at /about"));
            Assert.That(body, Does.Contain("edited"));
        });
    }

    private static async Task<string> PreviewAsync(PageHarness pages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/seed-apply");

        await ApplySeedsEndpoint.PreviewAsync(context, pages.Editor, TestOptions.Seeds, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> ApplyAsync(PageHarness pages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/seed-apply");

        await ApplySeedsEndpoint.ApplyAsync(context, pages.Editor, TestOptions.Seeds, NullLoggerFactory.Instance);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
