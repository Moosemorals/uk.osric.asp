// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageListingFoldTests {

    [Test]
    public void AFolderAboveAKeyIsAnsweredOnceAndBeforeTheKey() {
        PageListingKeys listing = Fold([Fact("/a/b")], prefix: "/");

        Assert.Multiple(() => {
            Assert.That(KeysOf(listing), Is.EqualTo(["/a", "/a/b"]));
            Assert.That(IsFolder(listing, "/a"), Is.True);
            Assert.That(IsFolder(listing, "/a/b"), Is.False);
        });
    }

    [Test]
    public void AKeyThatIsBothAPageAndAFolderIsOneKey() {
        PageListingKeys listing = Fold([Fact("/a"), Fact("/a/b")], prefix: "/");

        Assert.Multiple(() => {
            Assert.That(KeysOf(listing), Is.EqualTo(["/a", "/a/b"]));
            Assert.That(IsFolder(listing, "/a"), Is.True, "something lives below it");
            Assert.That(listing.Keys.Count(key => key.Key == "/a"), Is.EqualTo(1), "one key, not two rows");
        });
    }

    [Test]
    public void AKeyWithNothingBelowItIsNotAFolder() {
        PageListingKeys listing = Fold([Fact("/a"), Fact("/b")], prefix: "/");

        Assert.Multiple(() => {
            Assert.That(IsFolder(listing, "/a"), Is.False);
            Assert.That(IsFolder(listing, "/b"), Is.False);
        });
    }

    [Test]
    public void ThePrefixIsAnsweredAndIsAFolderWhenSomethingLivesBelow() {
        PageListingKeys together = Fold([Fact("/a"), Fact("/a/b")], prefix: "/a");
        PageListingKeys alone = Fold([Fact("/a")], prefix: "/a");

        Assert.Multiple(() => {
            Assert.That(KeysOf(together), Is.EqualTo(["/a", "/a/b"]));
            Assert.That(IsFolder(together, "/a"), Is.True);
            Assert.That(KeysOf(alone), Is.EqualTo(["/a"]));
            Assert.That(IsFolder(alone, "/a"), Is.False);
        });
    }

    [Test]
    public void AFolderAboveThePrefixIsNotAnswered() {
        PageListingKeys listing = Fold([Fact("/a/b/c")], prefix: "/a/b");

        Assert.That(KeysOf(listing), Is.EqualTo(["/a/b/c"]));
    }

    [Test]
    public void AKeyOutsideTheSubtreeIsNotAnswered() {
        PageListingKeys listing = Fold([Fact("/b"), Fact("/ab"), Fact("/a/c")], prefix: "/a");

        Assert.That(KeysOf(listing), Is.EqualTo(["/a/c"]));
    }

    [Test]
    public void ACursorAnswersOnlyWhatStandsAfterIt() {
        PageListingKeys listing = Fold([Fact("/a"), Fact("/a/b"), Fact("/a/c")], prefix: "/", after: "/a");

        Assert.That(KeysOf(listing), Is.EqualTo(["/a/b", "/a/c"]));
    }

    [Test]
    public void ASliceAfterAFolderAnswersItsChildrenAndNotTheFolder() {
        PageListingKeys listing = Fold([Fact("/a/b/c")], prefix: "/", after: "/a");

        Assert.Multiple(() => {
            Assert.That(KeysOf(listing), Is.EqualTo(["/a/b", "/a/b/c"]));
            Assert.That(IsFolder(listing, "/a/b"), Is.True);
        });
    }

    [Test]
    public void AOneLevelFoldAnswersThePrefixAndItsOwnChildren() {
        PageListingKeys listing = Fold(
            [Fact("/a"), Fact("/a/b"), Fact("/a/b/c"), Fact("/a/d"), Fact("/e")],
            prefix: "/a", recursive: false);

        Assert.Multiple(() => {
            Assert.That(KeysOf(listing), Is.EqualTo(["/a", "/a/b", "/a/d"]));
            Assert.That(IsFolder(listing, "/a/b"), Is.True, "it holds /a/b/c, which is not answered");
            Assert.That(IsFolder(listing, "/a/d"), Is.False);
        });
    }

    [Test]
    public void AFolderTheReadNamedIsAnsweredWithoutAKeyBelowIt() {
        PageListingKeys listing = Fold([Fact("/a/b", isFolder: true)], prefix: "/a", recursive: false);

        Assert.Multiple(() => {
            Assert.That(KeysOf(listing), Is.EqualTo(["/a/b"]));
            Assert.That(IsFolder(listing, "/a/b"), Is.True);
        });
    }

    [Test]
    public void TruncatedSaysWhetherAKeyStandsAfterTheLast() {
        PageListingKeys limited = Fold([Fact("/a"), Fact("/b"), Fact("/c")], prefix: "/", limit: 2);
        PageListingKeys whole = Fold([Fact("/a"), Fact("/b")], prefix: "/", limit: 2);

        Assert.Multiple(() => {
            Assert.That(limited.Keys, Has.Count.EqualTo(2));
            Assert.That(limited.Truncated, Is.True);
            Assert.That(whole.Truncated, Is.False);
        });
    }

    [Test]
    public void AWalkAnswersEveryKeyOnceAndAFolderFirst() {
        // /a sorts before /a!, and /a/b stands past the limit, so a slice of one must answer the folder
        // from the row it did not reach — and then never answer it again.
        PageListingKey[] facts = [Fact("/a!"), Fact("/a/b")];

        List<string> walk = [];
        string? cursor = null;
        while (true) {
            PageListingKeys listing = Fold(facts, prefix: "/", after: cursor, limit: 1);
            if (listing.Keys.Count == 0) {
                break;
            }

            walk.Add(listing.Keys[0].Key);
            cursor = listing.Keys[0].Key;
        }

        Assert.That(walk, Is.EqualTo(["/a", "/a!", "/a/b"]));
    }

    private static PageListingKey Fact(string key, bool isFolder = false) => new(key, isFolder);

    private static PageListingKeys Fold(
        IReadOnlyList<PageListingKey> facts, string prefix = "/", string? after = null,
        bool recursive = true, int limit = 100) =>
        PageListingFold.Of(facts, prefix, after, recursive, limit);

    private static string[] KeysOf(PageListingKeys listing) => [.. listing.Keys.Select(key => key.Key)];

    private static bool IsFolder(PageListingKeys listing, string key) =>
        listing.Keys.Single(node => node.Key == key).IsFolder;
}
