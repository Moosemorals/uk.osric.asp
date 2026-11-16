// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetSeederTests {

    private static readonly string[] SeedPaths = ["/_layout", "/_lib/document", "/_lib/editor", "/about", "/assistant.css", "/assistant.js", "/editor-mode", "/editor.css", "/editor.js", "/help", "/pages", "/privacy", "/site.css", "/sortable.css", "/sortable.js", "/vim-help", "/vim.css", "/vim.js"];

    private static readonly PageSeeds Pack = new(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));

    [Test]
    public void SeedPathsAreTheShippedFiles() {
        Assert.That(TestOptions.Seeds.Paths.Select(path => path.Key), Is.EquivalentTo(SeedPaths));
    }

    // A library has to be known as one the moment it is seeded, or the layout that names it cannot
    // be compiled on a fresh installation.
    [Test]
    public async Task ASeededLibraryIsStoredAsALibraryAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(storage["/_lib/document"]?.Kind, Is.EqualTo(PageKind.Library));
        Assert.That(storage["/about"]?.Kind, Is.EqualTo(PageKind.Page));
    }

    [Test]
    public async Task ASeededCreateRecordsTheSeedItInstalledAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(storage["/about"]?.SeedHash, Is.EqualTo(Pack.Find(PagePath.Normalize("/about"))?.Hash));
    }

    [Test]
    public async Task AKeyTheStoreAlreadyHasKeepsItsAnchorAsync() {
        FakePageStore storage = new();
        storage["/about"] = new PageTemplate(PagePath.Normalize("/about"), "mine");

        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(storage["/about"]?.SeedHash, Is.Null);
    }

    [Test]
    public async Task MissingAssetsAreSeededWithoutCompiledImagesAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage, Pack);

        foreach (string path in SeedPaths) {
            Assert.That(storage[path]?.Template, Is.EqualTo(ShippedSeed(path)), path);
            Assert.That(storage[path]?.Image, Is.Null, path);
        }
        Assert.That(storage.WriteCount, Is.EqualTo(SeedPaths.Length));
    }

    // The administration views' sheets and script, the status and reference sheets and the icon are
    // the web root's: a release that put one back in the pack would have two homes for the same file.
    [TestCase("/admin.css")]
    [TestCase("/admin.js")]
    [TestCase("/reference.css")]
    [TestCase("/rocket.svg")]
    [TestCase("/status.css")]
    public void AShellFileIsNotASeed(string path) {
        Assert.That(TestOptions.Seeds.Find(PagePath.Normalize(path)), Is.Null);
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task ShippedSeedCanBeReadByItsPagePathAsync(string path) {
        string? template = TestOptions.SeedSource(PagePath.Normalize(path));

        Assert.That(template, Is.EqualTo(ShippedSeed(path)));
    }

    [TestCase("/assistant.js")]
    [TestCase("/editor.js")]
    public async Task ShippedScriptsAreNativeModulesAsync(string path) {
        string? template = TestOptions.SeedSource(PagePath.Normalize(path));

        Assert.That(template, Does.Match(@"(?m)^(?:export|import)\b"));
    }

    [TestCase("/assistant.css")]
    [TestCase("/assistant.js")]
    [TestCase("/editor.css")]
    [TestCase("/editor.js")]
    [TestCase("/site.css")]
    public async Task StylesheetsAndScriptsCarryNoTemplateDirectiveAsync(string path) {
        string? template = TestOptions.SeedSource(PagePath.Normalize(path));

        Assert.That(template, Does.Not.Contain("<%"));
    }

    // A shipped page that warns would greet a fresh installation with "Compiled with warnings", so
    // the seeds are held to the standard the prompt's examples are.
    [TestCaseSource(nameof(SeedPaths))]
    public async Task EveryShippedSeedIsStoredWithoutDiagnosticsAsync(string path) {
        using PageHarness pages = new(new FakePageStore());
        await pages.SeedAsync();
        string template = (TestOptions.SeedSource(PagePath.Normalize(path)))!;
        IReadOnlyList<PageLibrary> libraries = await pages.Libraries.LiveAsync();

        PageImage image = pages.Images.Build(
            PagePath.Normalize(path), template, PageMetrics.ReasonSave, libraries, CancellationToken.None);

        Assert.That(image.Diagnostics, Is.Empty,
            $"{path}: {string.Join("; ", image.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        Assert.That(image.Bytes, image.Kind is PageKind.Page ? Is.Not.Null : Is.Null, path);
    }


    [Test]
    public async Task TheHelpPageIsAReachableFragment() {
        string? template = TestOptions.SeedSource(PagePath.Normalize("/help"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("IPageLayout").And.Contain("Title = \"Help\""));
        Assert.That(template, Does.Contain("<h1>Help</h1>"));
        Assert.That(template, Does.Contain("id=\"layout\"").And.Contain("id=\"syntax\"").And.Contain("id=\"context\""));
        Assert.That(template, Does.Contain("href=\"<%= ctx.EditorPath %>?edit="), "the editor link follows the configured key");
    }

    [Test]
    public async Task TheAboutPageIsAReachableFragment() {
        string? template = TestOptions.SeedSource(PagePath.Normalize("/about"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("Title = \"About\""));
        Assert.That(template, Does.Contain("<h1>About</h1>"));
        Assert.That(template, Does.Contain("Osric Wilkinson").And.Contain("DeepSeek"));
        Assert.That(template, Does.Contain("IBuildInfo"));
        Assert.That(template, Does.Contain("https://github.com/Moosemorals/uk.osric.asp/commit/"));
        Assert.That(template, Does.Contain("href=\"/admin/copyright\""),
            "the licence texts are the shell's, read from the files the build carries");
        Assert.That(template, Does.Not.Contain("RedirectToLogin"), "the about page is public");
    }

    [Test]
    public async Task ThePrivacyPageIsAPublicFragmentAsync() {
        string? template = TestOptions.SeedSource(PagePath.Normalize("/privacy"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("Title = \"Privacy\""));
        Assert.That(template, Does.Contain("<h1>Privacy</h1>"));
        Assert.That(template, Does.Contain("I don't care who you are, and I'm not tracking you."));
        Assert.That(template, Does.Not.Contain("ctx.User"), "the privacy page is public");
    }


    [Test]
    public async Task TheGuardedPageGuardsItself() {
        string? template = TestOptions.SeedSource(PagePath.Normalize("/pages"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("<h1>Pages</h1>"));
        Assert.That(template, Does.Contain("if (ctx.User is not { } user) { ctx.Response.RedirectToLogin(); return; }"));
        Assert.That(template, Does.Contain("IPageWrites").And.Contain("IPageCatalog").And.Contain("IDiffRenderer"));
        Assert.That(template, Does.Contain("ctx.Response.AllowScripts()"));
        Assert.That(template, Does.Contain("ctx.Response.AntiforgeryField"), "every form is checked by the host");
    }

    [Test]
    public async Task TheLayoutSeedIsAPageTheOwnerCanEdit() {
        string? template = TestOptions.SeedSource(PagePath.Normalize("/_layout"));

        Assert.That(template, Does.Contain("Document.Head"));
        Assert.That(template, Does.Contain("layout.Content"));
        Assert.That(new ReservedPaths(ReservedPaths.Default).IsReserved(PagePath.Normalize("/_layout")), Is.False);
    }

    [TestCase("/not-a-seed")]
    [TestCase("/admin/restore")]
    public async Task UnknownAndNestedPagesHaveNoShippedSeedAsync(string path) {
        string? template = TestOptions.SeedSource(PagePath.Normalize(path));

        Assert.That(template, Is.Null);
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task ExistingAssetsArePreservedAndSeedingIsIdempotentAsync(string editedPath) {
        const string editedTemplate = "body { color: rebeccapurple; }";
        FakePageStore storage = new();
        storage[editedPath] = new PageTemplate(PagePath.Normalize(editedPath), editedTemplate);

        await PageAssetSeeder.SeedAsync(storage, Pack);
        int writes = storage.WriteCount;
        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(storage[editedPath]?.Template, Is.EqualTo(editedTemplate));
        foreach (string path in SeedPaths.Where(path => path != editedPath)) {
            Assert.That(storage[path]?.Template, Is.EqualTo(ShippedSeed(path)), path);
        }
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task DeletedSeededAssetsAreNotReseededAsync(string deletedPath) {
        FakePageStore storage = new();
        storage[deletedPath] = new PageTemplate(PagePath.Normalize(deletedPath), "edited asset");
        await storage.DeletePageAsync(PagePath.Normalize(deletedPath));

        int writes = storage.WriteCount;
        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(await storage.GetPageAsync(PagePath.Normalize(deletedPath)), Is.Null);
        Assert.That((await storage.GetHeadAsync(PagePath.Normalize(deletedPath)))?.IsDeleted, Is.True);
        Assert.That(storage.WriteCount, Is.EqualTo(writes + SeedPaths.Length - 1), "only the never-created assets are seeded");
    }

    private static string ShippedSeed(string path) {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", path.TrimStart('/')));
    }
}
