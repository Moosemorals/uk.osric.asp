// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetSeederTests {

    private static readonly string[] SeedPaths = ["/_layout", "/admin", "/admin.css", "/admin.js", "/assistant.css", "/assistant.js", "/editor.css", "/editor.js", "/layout.css", "/rocket.svg", "/site.css", "/status.css"];

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
    [TestCase("/layout.css")]
    [TestCase("/site.css")]
    [TestCase("/status.css")]
    public async Task StylesheetsAndScriptsCarryNoTemplateDirectiveAsync(string path) {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize(path));

        Assert.That(template, Does.Not.Contain("<%"));
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
    public async Task TheLayoutSeedIsAPageTheOwnerCanEdit() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/_layout"));

        Assert.That(template, Does.Contain("PageDocument.Head"));
        Assert.That(template, Does.Contain("layout.Body"));
        Assert.That(new ReservedPaths(ReservedPaths.Default).IsReserved(PagePath.Normalize("/_layout")), Is.False);
    }

    [Test]
    public async Task TheSvgSeedKeepsItsOwnContentTypeAsync() {
        string? template = await PageAssetSeeder.ReadSeedTemplateAsync(PagePath.Normalize("/rocket.svg"));

        Assert.That(template, Does.Contain("ctx.SetContentType(\"image/svg+xml\")"));
        Assert.That(template, Does.Not.Contain("Cache-Control"));
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
