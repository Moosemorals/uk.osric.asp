// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ShellPatternAndSearchTests {

    [Test]
    public async Task APatternNamesEveryKeyItFits() {
        FakePageStore storage = new();
        storage["/vim/vim.css"] = new PageTemplate(PageHarness.Key("/vim/vim.css"), "alpha");
        storage["/vim/vim.js"] = new PageTemplate(PageHarness.Key("/vim/vim.js"), "needle one");
        storage["/vim/sub/deep.js"] = new PageTemplate(PageHarness.Key("/vim/sub/deep.js"), "needle deep");
        using PageHarness pages = new(storage);

        string listed = await ShellKernelHarness.OutputAsync(pages.Editor, "ls /vim/*.js");
        string catted = await ShellKernelHarness.OutputAsync(pages.Editor, "cat /vim/*.js");

        Assert.Multiple(() => {
            Assert.That(listed.Split('\n'), Is.EqualTo(["/vim/vim.js"]), "a star never crosses a /");
            Assert.That(catted, Is.EqualTo("needle one"), "the star stops at the segment: /vim/sub/deep.js is below it");
        });
    }

    [Test]
    public async Task AQuotedWildcardIsTheCharacter() {
        FakePageStore storage = new();
        storage["/notes*"] = new PageTemplate(PageHarness.Key("/notes*"), "the literal one");
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "the plain one");
        using PageHarness pages = new(storage);

        Assert.That(await ShellKernelHarness.OutputAsync(pages.Editor, "cat '/notes*'"),
            Is.EqualTo("the literal one"));
    }

    [Test]
    public async Task APatternThatFitsNothingIsARefusal() {
        FakePageStore storage = new();
        storage["/vim/vim.js"] = new PageTemplate(PageHarness.Key("/vim/vim.js"), "alpha");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "cat /vim/*.css");

        Assert.That(refusal, Does.Contain("No key matches /vim/*.css").And.Contain("ls /vim lists what is there"));
    }

    [Test]
    public async Task GrepReadsOneLevelUnlessItIsToldToDescend() {
        FakePageStore storage = new();
        storage["/top"] = new PageTemplate(PageHarness.Key("/top"), "needle here");
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "needle below");
        using PageHarness pages = new(storage);

        string flat = await ShellKernelHarness.OutputAsync(pages.Editor, "grep needle /");
        string deep = await ShellKernelHarness.OutputAsync(pages.Editor, "grep -r needle /");
        string empty = await ShellKernelHarness.OutputAsync(pages.Editor, "grep nothing /");

        Assert.Multiple(() => {
            Assert.That(flat.Split('\n')[0], Is.EqualTo("/top:1: needle here"));
            Assert.That(flat, Does.Not.Contain("needle below"));
            Assert.That(deep, Does.Contain("/folder/deep:1: needle below"));
            Assert.That(empty, Does.Contain("No match at this level.").And.Contain("grep -r reads the folders too"));
        });
    }

    [Test]
    public async Task GrepNamesTheKeysOrCountsThemLinesWhenAsked() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "needle\nneedle");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "nothing");
        storage["/c"] = new PageTemplate(PageHarness.Key("/c"), "needle");
        using PageHarness pages = new(storage);

        string names = await ShellKernelHarness.OutputAsync(pages.Editor, "grep -l needle /");
        string counts = await ShellKernelHarness.OutputAsync(pages.Editor, "grep -c needle /");

        Assert.Multiple(() => {
            Assert.That(names.Split('\n'), Is.EqualTo(["/a", "/c"]), "one key per page, however many lines match");
            Assert.That(counts.Split('\n'), Does.Contain("/a:2").And.Contain("/c:1"));
        });
    }

    [Test]
    public async Task WcHeadAndTailTakeMoreThanOneKeyAsPosixDoes() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one\ntwo");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "three");
        using PageHarness pages = new(storage);

        string counted = await ShellKernelHarness.OutputAsync(pages.Editor, "wc /a /b");
        string headed = await ShellKernelHarness.OutputAsync(pages.Editor, "head -n 1 /a /b");

        Assert.Multiple(() => {
            Assert.That(counted.Split('\n'), Is.EqualTo(["2 /a", "1 /b", "3 total"]));
            Assert.That(headed.Split('\n'), Is.EqualTo(["==> /a <==", "one", "", "==> /b <==", "three"]));
        });
    }

    [Test]
    public async Task FindTakesTestsAndJoinsThemWithAnd() {
        FakePageStore storage = new();
        storage["/vim/vim.js"] = new PageTemplate(PageHarness.Key("/vim/vim.js"), "alpha");
        storage["/vim/vim.css"] = new PageTemplate(PageHarness.Key("/vim/vim.css"), "alpha");
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "alpha");
        using PageHarness pages = new(storage);

        string byName = await ShellKernelHarness.OutputAsync(pages.Editor, "find /vim -name '*.js' -type f");
        string byType = await ShellKernelHarness.OutputAsync(pages.Editor, "find /vim -type d");

        Assert.Multiple(() => {
            Assert.That(byName, Is.EqualTo("/vim/vim.js"));
            Assert.That(byType, Is.EqualTo("/vim"), "the folder itself is at the path");
        });
    }

    [Test]
    public async Task FindRefusesATestItHasNotGot() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "find / -size 10");

        Assert.That(refusal, Does.Contain("-size is not a test this shell has").And.Contain("Run help find"));
    }
}
