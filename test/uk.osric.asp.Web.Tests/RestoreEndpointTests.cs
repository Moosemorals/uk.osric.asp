// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class RestoreEndpointTests {

    [Test]
    public async Task ThePreviewNamesEverySeedAndItsStateAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/about"] = new PageTemplate(Key("/about"), "changed by the owner");

        using PageHarness pages = new(storage);
        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("/about"));
            Assert.That(body, Does.Contain("changed"));
            Assert.That(body, Does.Contain("/editor.css"));
            Assert.That(body, Does.Contain("current"));
            Assert.That(body, Does.Contain("Restore the seed"), "one seed needs restoring, so the button says so");
        });
    }

    [Test]
    public async Task ThePageLoadsNothingTheOwnerCanBreakAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/about"] = new PageTemplate(Key("/about"), "changed by the owner");

        using PageHarness pages = new(storage);
        string preview = await PreviewAsync(pages);
        string result = await RestoreAsync(pages);

        foreach (string body in new[] { preview, result }) {
            Assert.Multiple(() => {
                Assert.That(body, Does.Not.Contain("<link"), "no stylesheet the owner can hide it with");
                Assert.That(body, Does.Not.Contain("<script"), "no script the owner can break it with");
                Assert.That(body, Does.Not.Contain("href=\"/site.css\""), "the seeded base sheet stays out of the recovery page");
                Assert.That(body, Does.Not.Contain("href=\"/admin.css\""), "the administration sheet stays out too");
                Assert.That(body, Does.Contain("<a href=\"#main\">Skip to content</a>"));
                Assert.That(body, Does.Contain("<main id=\"main\">"));
            });
        }
    }

    [Test]
    public async Task ThePreviewOffersNoButtonWhenEverySeedMatchesAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);

        using PageHarness pages = new(storage);
        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("<form"));
            Assert.That(body, Does.Contain("there is nothing to restore"));
        });
    }

    [Test]
    public async Task RestoringWritesOnlyTheSeedsThatDifferAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/about"] = new PageTemplate(Key("/about"), "changed by the owner");
        storage["/mine"] = new PageTemplate(Key("/mine"), "the owner's own page");
        int total = PageAssetSeeder.SeedPaths().Count;
        string about = await SeedOfAsync("/about");

        using PageHarness pages = new(storage);
        string body = await RestoreAsync(pages);

        Assert.Multiple(() => {
            Assert.That(storage.WriteCount, Is.EqualTo(1), "a seed that already matches is not written");
            Assert.That(storage["/about"]?.Template, Is.EqualTo(about));
            Assert.That(storage["/mine"]?.Template, Is.EqualTo("the owner's own page"));
            Assert.That(body, Does.Contain("1 restored"));
            Assert.That(body, Does.Contain($"{total - 1} already matched"));
        });
    }

    [Test]
    public async Task AMissingSeedComesBackAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage, except: "/about");
        string about = await SeedOfAsync("/about");

        using PageHarness pages = new(storage);
        string body = await RestoreAsync(pages);

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo(about));
            Assert.That(body, Does.Contain("missing"));
        });
    }

    [Test]
    public async Task ATombstonedSeedComesBackAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        string about = await SeedOfAsync("/about");

        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(Key("/about"));
        Assert.That((await storage.GetHeadAsync(Key("/about")))?.IsDeleted, Is.True);

        string body = await RestoreAsync(pages);
        bool deleted = (await storage.GetHeadAsync(Key("/about")))?.IsDeleted ?? true;

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo(about));
            Assert.That(deleted, Is.False, "the tombstone is cleared");
            Assert.That(body, Does.Contain("deleted"));
        });
    }

    [Test]
    public async Task ASeedThatFailsDoesNotStopTheOthersAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/about"] = new PageTemplate(Key("/about"), "changed by the owner");
        storage["/editor.css"] = new PageTemplate(Key("/editor.css"), "changed by the owner");
        string editorCss = await SeedOfAsync("/editor.css");

        FakePageRevisionStore revisions = new(storage) {
            OnSave = revision => {
                if (revision.Path == Key("/about")) {
                    throw new InvalidOperationException("the disk is full");
                }
            },
        };

        using PageHarness pages = new(storage, revisionStore: revisions);
        string body = await RestoreAsync(pages);

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo("changed by the owner"), "the failed page is unchanged");
            Assert.That(storage["/editor.css"]?.Template, Is.EqualTo(editorCss));
            Assert.That(body, Does.Contain("1 restored"));
            Assert.That(body, Does.Contain("1 failed"));
        });
    }

    private static async Task SeedAllAsync(FakePageStore storage, string? except = null) {
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            if (string.Equals(path.Key, except, StringComparison.Ordinal)) {
                continue;
            }

            storage[path.Key] = new PageTemplate(path, await SeedOfAsync(path.Key));
        }
    }

    private static async Task<string> SeedOfAsync(string path) =>
        (await PageAssetSeeder.ReadSeedTemplateAsync(Key(path)))!;

    private static async Task<string> PreviewAsync(PageHarness pages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/restore");
        await RestoreEndpoint.PreviewAsync(context, pages.Editor, new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> RestoreAsync(PageHarness pages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/restore");
        await RestoreEndpoint.RestoreAsync(context, pages.Editor, NullLoggerFactory.Instance);
        return await TestHttp.ReadBodyAsync(context);
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
