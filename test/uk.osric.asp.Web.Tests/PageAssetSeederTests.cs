// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetSeederTests {

    [Test]
    public async Task MissingAssetsAreSeededWithoutCompiledImagesAsync() {
        FakePageStore storage = new();

        await PageAssetSeeder.SeedAsync(storage);

        Assert.That(storage["/site.css"]?.Content, Is.EqualTo(PageAssetSeeder.StylesheetTemplate));
        Assert.That(storage["/site.css"]?.Compilation, Is.Null);
        Assert.That(storage["/rocket.svg"]?.Content, Is.EqualTo(PageAssetSeeder.IconTemplate));
        Assert.That(storage["/rocket.svg"]?.Compilation, Is.Null);
        Assert.That(storage.WriteCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ExistingAssetsArePreservedAndSeedingIsIdempotentAsync() {
        const string editedStylesheet = "body { color: rebeccapurple; }";
        FakePageStore storage = new();
        storage["/site.css"] = new StoredPage(PagePath.Normalize("/site.css"), editedStylesheet);

        await PageAssetSeeder.SeedAsync(storage);
        int writes = storage.WriteCount;
        await PageAssetSeeder.SeedAsync(storage);

        Assert.That(storage["/site.css"]?.Content, Is.EqualTo(editedStylesheet));
        Assert.That(storage["/rocket.svg"]?.Content, Is.EqualTo(PageAssetSeeder.IconTemplate));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
    }
}
