// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetSeederTests {

    private static readonly string[] SeedPaths = ["/editor.css", "/editor.js", "/rocket.svg", "/site.css", "/status.css"];

    [Test]
    public async Task MissingAssetsAreSeededWithoutCompiledImagesAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage);

        foreach (string path in SeedPaths) {
            Assert.That(storage[path]?.Content, Is.EqualTo(ShippedSeed(path)), path);
            Assert.That(storage[path]?.Compilation, Is.Null, path);
        }
        Assert.That(storage.WriteCount, Is.EqualTo(SeedPaths.Length));
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task ExistingAssetsArePreservedAndSeedingIsIdempotentAsync(string editedPath) {
        const string editedContent = "body { color: rebeccapurple; }";
        FakePageStore storage = new();
        storage[editedPath] = new StoredPage(PagePath.Normalize(editedPath), editedContent);

        await PageAssetSeeder.SeedAsync(storage);
        int writes = storage.WriteCount;
        await PageAssetSeeder.SeedAsync(storage);

        Assert.That(storage[editedPath]?.Content, Is.EqualTo(editedContent));
        foreach (string path in SeedPaths.Where(path => path != editedPath)) {
            Assert.That(storage[path]?.Content, Is.EqualTo(ShippedSeed(path)), path);
        }
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
    }

    [TestCaseSource(nameof(SeedPaths))]
    public async Task DeletedSeededAssetsAreNotReseededAsync(string deletedPath) {
        FakePageStore storage = new();
        storage[deletedPath] = new StoredPage(PagePath.Normalize(deletedPath), "edited asset");
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
