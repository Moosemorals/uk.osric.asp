// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;

/// <summary>Proves the changelist is one read, and that the route keeps the reason a page does not.</summary>
[TestFixture]
public sealed class ChangeListTests {

    private static readonly string[] ExpectedSubjects = ["First", "Second"];

    private readonly List<string> _paths = [];

    [TearDown]
    public void RemoveFiles() {
        foreach (string path in _paths) {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            } else {
                File.Delete(path);
            }
        }

        _paths.Clear();
    }

    [Test]
    public void AFullHashAndItsSubjectBecomeACommit() {
        IReadOnlyList<SiteCommit> commits = ChangeList.Parse(
            "0123456789abcdef0123456789abcdef01234567 First commit\n");

        Assert.That(commits.Single(), Is.EqualTo(
            new SiteCommit("0123456789abcdef0123456789abcdef01234567", "First commit")));
    }

    [Test]
    public void ALineThatIsNotACommitKeepsItsTextAndHasNoHash() {
        IReadOnlyList<SiteCommit> commits = ChangeList.Parse("not a commit line\n");

        Assert.That(commits.Single(), Is.EqualTo(new SiteCommit(null, "not a commit line")));
    }

    [Test]
    public void BlankLinesAreSkippedAndTheOrderIsKept() {
        IReadOnlyList<SiteCommit> commits = ChangeList.Parse(
            "0123456789abcdef0123456789abcdef01234567 First\n\nfedcba9876543210fedcba9876543210fedcba98 Second\n");

        Assert.That(commits.Select(commit => commit.Subject), Is.EqualTo(ExpectedSubjects));
    }

    [Test]
    public async Task APresentFileIsOkEvenWhenItHasNoLinesAsync() {
        ChangeListRead read = await ReadAsync("\n\n");

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ChangeListReason.Ok));
            Assert.That(read.Commits, Is.Empty, "empty is a fact, not an absence");
        });
    }

    [Test]
    public async Task AMissingFileIsMissingAndHasNoCommitsAsync() {
        ChangeListRead read = await ReadAsync(contents: null);

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ChangeListReason.Missing));
            Assert.That(read.Commits, Is.Empty);
        });
    }

    // A folder where a file is expected fails the same way on every platform the suite runs on.
    [Test]
    public void AnUnreadableFileIsUnreadableAndHasNoCommits() {
        string path = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changes.dir.{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(path);
        _paths.Add(path);

        ChangeListRead read = new ChangeList(new AdminOptions { Changelist = path }, NullLogger<ChangeList>.Instance).Read;

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ChangeListReason.Unreadable));
            Assert.That(read.Commits, Is.Empty);
        });
    }

    [Test]
    public void ARelativeSettingIsResolvedBesideTheAssembly() {
        string path = ChangeList.PathOf(new AdminOptions { Changelist = "changes.txt" });

        Assert.That(path, Is.EqualTo(Path.Combine(AppContext.BaseDirectory, "changes.txt")));
    }

    [Test]
    public async Task TheFileIsReadOnceAsync() {
        string path = await CreateAsync("0123456789abcdef0123456789abcdef01234567 First\n");
        ChangeList list = new(new AdminOptions { Changelist = path }, NullLogger<ChangeList>.Instance);

        ChangeListRead first = list.Read;
        await File.WriteAllTextAsync(path, string.Empty);
        ChangeListRead second = list.Read;

        Assert.That(second, Is.SameAs(first), "the process holds the answer it read first");
    }

    private async Task<ChangeListRead> ReadAsync(string? contents) {
        string path = contents is null
            ? Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changes.missing.{Guid.NewGuid():N}.txt")
            : await CreateAsync(contents);

        return new ChangeList(new AdminOptions { Changelist = path }, NullLogger<ChangeList>.Instance).Read;
    }

    private async Task<string> CreateAsync(string contents) {
        string path = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changes.{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, contents);
        _paths.Add(path);

        return path;
    }
}
