// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>What a page sees of a library, and what a library change costs the pages that named it.</summary>
[TestFixture]
public sealed class PageLibraryClosureTests {

    private const string Document =
        "<%! namespace Site { public static class Document { public static string Head() => \"one\"; } } %>";

    private const string Uses = "<%! using Site; %><% ctx.Write(Document.Head()); %>";

    [Test]
    public async Task APageRendersWhatTheLibraryItsUsingNamesReturnsAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.ReadStore.Save("/_lib/document", Document, templateKind: PageKind.Library);
        await pages.StoreCompiledAsync("/uses", Uses);

        (RenderResult result, string output) = await pages.RenderAsync("/uses");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("one"));
    }

    [Test]
    public async Task ALibraryChangeRecompilesOnlyThePagesThatNameItAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.ReadStore.Save("/_lib/document", Document, templateKind: PageKind.Library);
        await pages.StoreCompiledAsync("/uses", Uses);
        await pages.StoreCompiledAsync("/plain", "<% ctx.Write(\"plain\"); %>");
        int compiles = pages.Compiler.CompileCount;

        // The library moves: its revision hash and its answer both change.
        pages.ReadStore.Save("/_lib/document",
            "<%! namespace Site { public static class Document { public static string Head() => \"two\"; } } %>",
            templateKind: PageKind.Library);

        (RenderResult changed, string output) = await pages.RenderAsync("/uses");
        (RenderResult untouched, _) = await pages.RenderAsync("/plain");

        Assert.Multiple(() => {
            Assert.That(changed.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo("two"), "the page that named the library was compiled again");
            Assert.That(untouched.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(pages.Compiler.CompileCount, Is.EqualTo(compiles + 1),
                "only the page whose closure moved recompiled");
        });
    }

    [Test]
    public async Task ALibraryWithABannedApiFailsThePageThatNamesItAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.ReadStore.Save("/_lib/document",
            "<%! namespace Site { public static class Document { public static string Read() => System.IO.File.ReadAllText(\"x\"); } } %>",
            templateKind: PageKind.Library);
        await pages.StoreCompiledAsync("/uses", Uses);

        (RenderResult result, _) = await pages.RenderAsync("/uses");

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.CompileFailed));
            Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
                diagnostic => diagnostic is not null
                    && diagnostic.Source == "/_lib/document"
                    && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task AStoredImageRecordsTheFingerprintOfItsClosureAsync() {
        using PageHarness pages = new(new FakePageStore());
        pages.ReadStore.Save("/_lib/document", Document, templateKind: PageKind.Library);

        await pages.StoreCompiledAsync("/uses", Uses);

        PageTemplate stored = PageHarness.Stored(pages.Storage, "/uses");
        Assert.That(stored.Image?.Libraries, Does.Contain("/_lib/document@"));
    }
}
