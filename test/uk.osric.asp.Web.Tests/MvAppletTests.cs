// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant.Shell;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class MvAppletTests {

    [Test]
    public async Task MvMovesThePageAndAnswersTheNewKeyAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        using PageHarness pages = new(storage);

        ShellOutcome outcome = await ShellKernelHarness.RunAsync(pages.Editor, "mv /a /b");

        Assert.Multiple(() => {
            Assert.That(outcome.IsSuccess, Is.True, outcome.Error);
            Assert.That(outcome.Output, Is.EqualTo("/a -> /b"));
            Assert.That(outcome.Changed?.Key, Is.EqualTo("/b"), "the tool result names the key a move made");
            Assert.That(storage["/a"], Is.Null);
            Assert.That(storage["/b"]?.Template, Is.EqualTo("one"));
        });
    }

    [Test]
    public async Task MvRefusesAMissingPageAsync() {
        using PageHarness pages = new(new FakePageStore());

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /missing /b");

        Assert.That(refusal, Does.Contain("No key /missing is stored"));
    }

    [Test]
    public async Task MvRefusesALiveTargetAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "two");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /a /b");

        Assert.Multiple(() => {
            Assert.That(refusal, Does.Contain("/b already has a page"));
            Assert.That(storage["/a"]?.Template, Is.EqualTo("one"));
        });
    }

    [Test]
    public async Task MvRefusesAKeyWithPagesBelowItAsync() {
        FakePageStore storage = new();
        storage["/folder"] = new PageTemplate(PageHarness.Key("/folder"), "the folder's own page");
        storage["/folder/child"] = new PageTemplate(PageHarness.Key("/folder/child"), "the child");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /folder /other");

        Assert.That(refusal, Does.Contain("/folder/child"));
    }

    [Test]
    public async Task MvRefusesALibraryAsync() {
        FakePageStore storage = new();
        storage["/_lib/thing"] = new PageTemplate(PageHarness.Key("/_lib/thing"), "public static class Thing { }") {
            Kind = PageKind.Library,
        };
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /_lib/thing /_lib/other");

        Assert.That(refusal, Does.Contain("library"));
    }

    [Test]
    public async Task MvRefusesAWildcardSourceAsync() {
        FakePageStore storage = new();
        storage["/alpha"] = new PageTemplate(PageHarness.Key("/alpha"), "one");
        storage["/alpine"] = new PageTemplate(PageHarness.Key("/alpine"), "two");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /al* /moved");

        Assert.Multiple(() => {
            Assert.That(refusal, Does.Contain("not a pattern"));
            Assert.That(storage["/alpha"]?.Template, Is.EqualTo("one"));
            Assert.That(storage["/alpine"]?.Template, Is.EqualTo("two"));
        });
    }

    [Test]
    public async Task MvRefusesAShellFileAsTheDestinationAsync() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "mv /a /login");

        Assert.That(refusal, Does.Contain("shell file"));
    }

    [Test]
    public async Task HelpNamesTheMoveCommandAsync() {
        using PageHarness pages = new(new FakePageStore());

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "help mv");

        Assert.That(output, Does.Contain("mv <key> <new-key>"));
    }
}
