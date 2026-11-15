// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageSeedsTests {

    private static readonly string[] Keys = ["/_layout", "/_lib/document", "/site.css"];
    private static readonly string[] Names = ["_layout", "_lib/document", "site.css"];

    [Test]
    public void ThePackIsEveryFileInKeyOrder() {
        using SeedFolder folder = new(("_layout", "layout"), ("_lib/document", "document"), ("site.css", "css"));

        PageSeeds seeds = new(folder.Directory);

        Assert.That(seeds.Paths.Select(path => path.Key), Is.EqualTo(Keys));
        Assert.That(seeds.Files.Select(seed => seed.Name), Is.EqualTo(Names));
    }

    [Test]
    public void ASeedKeepsTheExactBytesItWasWrittenWith() {
        const string source = "a\r\nb\uFEFFc";

        using SeedFolder folder = new(("about", source));
        PageSeeds seeds = new(folder.Directory);

        Assert.That(seeds.Source(PagePath.Normalize("/about")), Is.EqualTo(source));
    }

    [Test]
    public void AKeyThePackDoesNotShipIsNotASeed() {
        using SeedFolder folder = new(("about", "mine"));

        PageSeeds seeds = new(folder.Directory);

        Assert.That(seeds.Contains(PagePath.Normalize("/missing")), Is.False);
        Assert.That(seeds.Find(PagePath.Normalize("/missing")), Is.Null);
        Assert.That(seeds.Source(PagePath.Normalize("/missing")), Is.Null);
    }

    [Test]
    public void ASeedIsNamedByItsPathUnderTheFolder() {
        using SeedFolder folder = new(("_lib/document", "document"));

        PageSeeds seeds = new(folder.Directory);

        Assert.That(seeds.Find(PagePath.Normalize("/_lib/document"))?.Name, Is.EqualTo("_lib/document"));
    }
}
