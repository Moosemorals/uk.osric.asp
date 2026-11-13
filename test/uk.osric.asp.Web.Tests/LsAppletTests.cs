// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class LsAppletTests {

    [Test]
    public async Task NamesWhatIsAtThePath() {
        FakePageStore storage = new();
        storage["/a/one"] = new PageTemplate(PageHarness.Key("/a/one"), "alpha");
        storage["/a/two"] = new PageTemplate(PageHarness.Key("/a/two"), "alpha");
        storage["/a/deep/three"] = new PageTemplate(PageHarness.Key("/a/deep/three"), "alpha");
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "ls /a");

        Assert.That(output.Split('\n'), Is.EqualTo((string[])["/a/deep", "/a/one", "/a/two"]),
            "what is at /a, and not what is below it");
    }

    [Test]
    public async Task TheLongListingSaysWhichEntriesAreFolders() {
        FakePageStore storage = new();
        storage["/a/one"] = new PageTemplate(PageHarness.Key("/a/one"), "alpha");
        storage["/a/deep/three"] = new PageTemplate(PageHarness.Key("/a/deep/three"), "alpha");
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "ls -l /a");

        Assert.Multiple(() => {
            Assert.That(output.Split('\n'), Does.Contain("drwxr-xr-x /a/deep"));
            Assert.That(output.Split('\n'), Does.Contain("-rw-r--r-- /a/one"));
        });
    }

    [Test]
    public async Task DescendsAndReversesWhenAsked() {
        FakePageStore storage = new();
        storage["/d/a"] = new PageTemplate(PageHarness.Key("/d/a"), "alpha");
        storage["/d/b"] = new PageTemplate(PageHarness.Key("/d/b"), "alpha");
        using PageHarness pages = new(storage);

        string recursive = await ShellKernelHarness.OutputAsync(pages.Editor, "ls -R /");
        string reversed = await ShellKernelHarness.OutputAsync(pages.Editor, "ls -r /d");
        string clustered = await ShellKernelHarness.OutputAsync(pages.Editor, "ls -Rr /d");

        Assert.Multiple(() => {
            Assert.That(recursive.Split('\n'), Does.Contain("/d/a").And.Contain("/d/b"));
            Assert.That(reversed.Split('\n'), Is.EqualTo((string[])["/d/b", "/d/a"]));
            Assert.That(clustered.Split('\n'), Is.EqualTo((string[])["/d/b", "/d/a"]), "-Rr is two flags");
        });
    }

    [Test]
    public async Task NamesThePageWhenThePathIsOne() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "alpha");
        using PageHarness pages = new(storage);

        Assert.That(await ShellKernelHarness.OutputAsync(pages.Editor, "ls /notes"), Is.EqualTo("/notes"),
            "ls on a page names the page, as a shell's ls does");
    }

    [Test]
    public async Task TakesSeveralPathsInTheOrderTheyAreWritten() {
        FakePageStore storage = new();
        storage["/a/one"] = new PageTemplate(PageHarness.Key("/a/one"), "alpha");
        storage["/b/two"] = new PageTemplate(PageHarness.Key("/b/two"), "alpha");
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "ls /b /a");

        Assert.That(output.Split('\n'), Is.EqualTo((string[])["/b/two", "/a/one"]));
    }

    [Test]
    public async Task AnAbsentPathIsARefusalThatSaysWhereToLook() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "alpha");
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "ls /empty");

        Assert.That(refusal, Does.Contain("No key /empty is stored").And.Contain("ls / lists what is there"));
    }

    [Test]
    public async Task TheRootListsTheRoutesTheSiteAnswers() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string output = await ShellKernelHarness.OutputAsync(pages.Editor, "ls /");

        Assert.Multiple(() => {
            Assert.That(output.Split('\n'), Does.Contain("/login"));
            Assert.That(output.Split('\n'), Does.Contain("/admin"));
            Assert.That(output.Split('\n'), Does.Not.Contain("/edit"));
            Assert.That(output.Split('\n'), Does.Not.Contain("/"), "a folder lists what is in it, not itself");
        });
    }
}
