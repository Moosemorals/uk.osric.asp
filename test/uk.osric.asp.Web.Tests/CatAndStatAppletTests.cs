// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class CatAndStatAppletTests {

    [Test]
    public async Task CatPrintsEachPageInTurn() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "two");
        using PageHarness pages = new(storage);

        Assert.That(await ShellKernelHarness.OutputAsync(pages.Editor, "cat /a /b"), Is.EqualTo("one\ntwo"));
    }

    [Test]
    public async Task CatNumbersEachPageFromOne() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one\ntwo");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "three");
        using PageHarness pages = new(storage);

        Assert.That(await ShellKernelHarness.OutputAsync(pages.Editor, "cat -n /a /b"),
            Is.EqualTo("1\tone\n2\ttwo\n1\tthree"),
            "a line number names a line of a page, so each page counts from one");
    }

    [Test]
    public async Task CatRefusesAFolder() {
        FakePageStore storage = new();
        storage["/folder/deep"] = new PageTemplate(PageHarness.Key("/folder/deep"), "alpha");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "cat /folder");

        Assert.Multiple(() => {
            Assert.That(refusal, Does.Contain("/folder is a folder, not a page"));
            Assert.That(refusal, Does.Contain("ls /folder"));
        });
    }

    [Test]
    public async Task CatReadsAShellFilesRecord() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "cat /login");

        Assert.Multiple(() => {
            Assert.That(output, Does.StartWith("/login\n"));
            Assert.That(output, Does.Contain("description:"));
        });
    }

    [Test]
    public async Task StatPrintsARecordPerKey() {
        FakePageStore storage = new();
        storage["/a"] = new PageTemplate(PageHarness.Key("/a"), "one");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "two");
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "stat /a /b");

        Assert.Multiple(() => {
            Assert.That(output.Split('\n'), Does.Contain("/a").And.Contain("/b"));
            Assert.That(output.Split('\n'), Does.Contain("-rw-r--r--"));
            Assert.That(output, Does.Not.Contain("shell file"), "both are pages");
        });
    }

    [Test]
    public async Task StatSaysWhenTheKeyIsAShellFile() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "stat /login");

        Assert.Multiple(() => {
            Assert.That(output, Does.StartWith("/login\n-r--r--r--\nshell file\n"));
            Assert.That(output, Does.Contain("lines,"));
        });
    }
}
