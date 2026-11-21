// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Compiles the editor page once, before the fixtures start, so they do not race for it.</summary>
/// <remarks>
/// The first compile in a process is where Roslyn and the page policy are jitted, which costs a
/// couple of seconds. Left to the fixtures, a dozen of them reach their first page at the same
/// moment and the one compile they share is stretched by their own contention; run here it is paid
/// once, serially, and the editor page - the one most fixtures render and the largest page the site
/// ships - is a cache hit for every one of them.
/// </remarks>
[SetUpFixture]
public sealed class WarmUp {

    [OneTimeSetUp]
    public async Task CompileTheEditorPageOnceAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.MirrorSeededLibrariesAsync();
        PagePath key = PageHarness.Key("/pages");
        IReadOnlyList<PageLibrary> libraries = await pages.Libraries.LiveAsync();
        PageImage image = pages.Images.Build(key, TestOptions.Seeds.Source(key)!, PageMetrics.ReasonLazy, libraries, CancellationToken.None);

        Assert.That(image.Diagnostics, Is.Empty,
            string.Join("; ", image.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }
}
