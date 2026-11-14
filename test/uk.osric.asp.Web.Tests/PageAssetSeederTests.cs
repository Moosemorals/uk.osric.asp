// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetSeederTests {

    private static readonly string[] SeedPaths = ["/_layout", "/about", "/admin", "/admin.css", "/admin.js", "/assistant.css", "/assistant.js", "/editor.css", "/editor.js", "/help", "/pages", "/privacy", "/recent", "/rocket.svg", "/scratch", "/site.css", "/status.css"];

    [Test]
    public void SeedPathsAreTheShippedFiles() {
        Assert.That(PageAssetSeeder.SeedPaths().Select(path => path.Key), Is.EquivalentTo(SeedPaths));
    }

    [Test]
    public async Task MissingAssetsAreSeededWithoutCompiledImagesAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage);

        foreach (string path in SeedPaths) {
            Assert.That(storage[path]?.Template, Is.EqualTo(ShippedSeed(path)), path);
            Assert.That(storage[path]?.Image, Is.Null, path);
        }
        Assert.That(storage.WriteCount, Is.EqualTo(SeedPaths.Length));
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task ShippedSeedCanBeReadByItsPagePathAsync(string path) {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

        Assert.That(template, Is.EqualTo(ShippedSeed(path)));
    }

    [TestCase("/admin.js")]
    [TestCase("/assistant.js")]
    [TestCase("/editor.js")]
    public async Task ShippedScriptsAreNativeModulesAsync(string path) {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

        Assert.That(template, Does.Match(@"(?m)^(?:export|import)\b"));
    }

    [TestCase("/admin.css")]
    [TestCase("/admin.js")]
    [TestCase("/assistant.css")]
    [TestCase("/assistant.js")]
    [TestCase("/editor.css")]
    [TestCase("/editor.js")]
    [TestCase("/site.css")]
    [TestCase("/status.css")]
    public async Task StylesheetsAndScriptsCarryNoTemplateDirectiveAsync(string path) {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

        Assert.That(template, Does.Not.Contain("<%"));
    }

    // A shipped page that warns would greet a fresh installation with "Compiled with warnings", so
    // the seeds are held to the standard the prompt's examples are.
    [TestCaseSource(nameof(SeedPaths))]
    public async Task EveryShippedSeedIsStoredWithoutDiagnosticsAsync(string path) {
        using PageHarness pages = new(new FakePageStore());
        string template = (await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path)))!;

        PageImage image = pages.Images.Build(
            PagePath.Normalize(path), template, PageMetrics.ReasonSave, [], CancellationToken.None);

        Assert.That(image.Diagnostics, Is.Empty,
            $"{path}: {string.Join("; ", image.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        Assert.That(image.Bytes, image.Kind is PageKind.Page ? Is.Not.Null : Is.Null, path);
    }

    [Test]
    public async Task TheAdminIndexIsAReachableFragment() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/admin"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("<h1>Administration</h1>"));
        Assert.That(template, Does.Contain("/admin/import").And.Contain("/admin/export").And.Contain("/status"));
    }

    [Test]
    public async Task TheHelpPageIsAReachableFragment() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/help"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("IPageLayout").And.Contain("Title = \"Help\""));
        Assert.That(template, Does.Contain("<h1>Help</h1>"));
        Assert.That(template, Does.Contain("id=\"layout\"").And.Contain("id=\"syntax\"").And.Contain("id=\"context\""));
        Assert.That(template, Does.Contain("href=\"<%= ctx.EditorPath %>?edit="), "the editor link follows the configured key");
    }

    [Test]
    public async Task TheAboutPageIsAReachableFragment() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/about"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("Title = \"About\""));
        Assert.That(template, Does.Contain("<h1>About</h1>"));
        Assert.That(template, Does.Contain("Osric Wilkinson").And.Contain("DeepSeek"));
        Assert.That(template, Does.Contain("IBuildInfo"));
        Assert.That(template, Does.Contain("https://github.com/Moosemorals/uk.osric.asp/commit/"));
        Assert.That(template, Does.Contain("The MIT licence").And.Contain("The ISC licence"));
        Assert.That(template, Does.Contain("Acornima 1.9.1").And.Contain("Markdig 1.6.0"));
        Assert.That(template, Does.Not.Contain("RedirectToLogin"), "the about page is public");
    }

    [Test]
    public async Task ThePrivacyPageIsAPublicFragmentAsync() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/privacy"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("Title = \"Privacy\""));
        Assert.That(template, Does.Contain("<h1>Privacy</h1>"));
        Assert.That(template, Does.Contain("I don't care who you are, and I'm not tracking you."));
        Assert.That(template, Does.Not.Contain("ctx.User"), "the privacy page is public");
    }

    [Test]
    public async Task ThePublicListingIsAReachableFragment() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/recent"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("<h1>Recent changes</h1>"));
        Assert.That(template, Does.Contain("IPageCatalog"));
        Assert.That(template, Does.Contain("href=\"<%= ctx.EditorPath %>"), "the editor link follows the configured key");
        Assert.That(template, Does.Not.Contain("RedirectToLogin"), "a public listing must not guard itself");
    }

    [Test]
    public async Task TheGuardedPageGuardsItself() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/pages"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("<h1>Pages</h1>"));
        Assert.That(template, Does.Contain("if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }"));
        Assert.That(template, Does.Contain("IPageEditor").And.Contain("IPageCatalog").And.Contain("IDiffRenderer"));
        Assert.That(template, Does.Contain("ctx.AllowScripts()"));
        Assert.That(template, Does.Contain("ctx.AntiforgeryField"), "every form is checked by the host");
    }

    [Test]
    public async Task TheDraftShowsItsOwnSourceAndSavesItself() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/scratch"));

        Assert.That(template, Is.Not.Null);
        Assert.That(template, Does.Not.Contain("<!DOCTYPE"));
        Assert.That(template, Does.Contain("<h1>Scratch</h1>"));
        Assert.That(template, Does.Contain("if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }"),
            "the draft guards its own view, so an anonymous visitor is sent to sign in");
        Assert.That(template, Does.Contain("IPageCatalog").And.Contain("IPageWriter"));
        Assert.That(template, Does.Contain("name=\"template\""));
        Assert.That(template, Does.Contain("ctx.AntiforgeryField"));
        Assert.That(template, Does.Contain("href=\"/help#writing\""));
    }

    [Test]
    public async Task TheLayoutSeedIsAPageTheOwnerCanEdit() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/_layout"));

        Assert.That(template, Does.Contain("PageDocument.Head"));
        Assert.That(template, Does.Contain("layout.Body"));
        Assert.That(new ReservedPaths(ReservedPaths.Default).IsReserved(PagePath.Normalize("/_layout")), Is.False);
    }

    [Test]
    public async Task TheSvgSeedReliesOnItsExtensionForItsContentTypeAsync() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/rocket.svg"));

        Assert.That(template, Does.Not.Contain("ctx.SetContentType"));
        Assert.That(template, Does.Not.Contain("<%"), "the icon has no code, so it is an asset");
        Assert.That(TestContentTypes.Shipped.ForPath("/rocket.svg"), Is.EqualTo("image/svg+xml; charset=utf-8"));
    }

    [TestCase("/not-a-seed")]
    [TestCase("/admin/restore")]
    public async Task UnknownAndNestedPagesHaveNoShippedSeedAsync(string path) {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

        Assert.That(template, Is.Null);
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task ExistingAssetsArePreservedAndSeedingIsIdempotentAsync(string editedPath) {
        const string editedTemplate = "body { color: rebeccapurple; }";
        FakePageStore storage = new();
        storage[editedPath] = new PageTemplate(PagePath.Normalize(editedPath), editedTemplate);

        await PageAssetSeeder.SeedAsync(storage);
        int writes = storage.WriteCount;
        await PageAssetSeeder.SeedAsync(storage);

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
        await PageAssetSeeder.SeedAsync(storage);

        Assert.That(await storage.GetPageAsync(PagePath.Normalize(deletedPath)), Is.Null);
        Assert.That((await storage.GetHeadAsync(PagePath.Normalize(deletedPath)))?.IsDeleted, Is.True);
        Assert.That(storage.WriteCount, Is.EqualTo(writes + SeedPaths.Length - 1), "only the never-created assets are seeded");
    }

    private static string ShippedSeed(string path) {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", path.TrimStart('/')));
    }
}
