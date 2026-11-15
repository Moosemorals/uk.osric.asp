// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Formats.Tar;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SeedCommandsTests {

    private static readonly (string Name, string Source)[] Edited = [("edited", "mine")];
    private static readonly (string Name, string Source)[] EveryLive = [("about", "same"), ("edited", "mine")];

    [Test]
    public async Task ListSaysWhatEachSeedIsRelativeToTheReleaseAsync() {
        using SeedFolder folder = new(("about", "same"), ("edited", "seed"), ("gone", "gone"), ("missing", "x"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same", seedHash: TemplateFingerprint.Of("same"));
        store.Save("/edited", "mine", seedHash: TemplateFingerprint.Of("seed"));
        store.Save("/gone", "gone", seedHash: TemplateFingerprint.Of("gone"));
        store.Delete("/gone");

        (byte[] output, string error, int code) = await RunAsync(seeds, store, ["list"]);

        Assert.That(code, Is.Zero);
        Assert.That(Encoding.UTF8.GetString(output),
            Is.EqualTo("/about\tin sync\n/edited\tedited\n/gone\tdeleted\n/missing\tnew\n"));
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task ListReportsAPageWhoseSeedTheReleaseDroppedAsync() {
        using SeedFolder folder = new(("about", "same"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same", seedHash: TemplateFingerprint.Of("same"));
        string dropped = TemplateFingerprint.Of("old-shape");
        store.Save("/old", "old-shape", seedHash: dropped);

        (byte[] output, _, int code) = await RunAsync(seeds, store, ["list"]);

        Assert.That(code, Is.Zero);
        Assert.That(Encoding.UTF8.GetString(output), Does.Contain("/old\tretired"));
    }

    [Test]
    public async Task PullWritesOnlyTheChangedSeedAsync() {
        using SeedFolder folder = new(("about", "same"), ("edited", "seed"), ("missing", "x"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same");
        store.Save("/edited", "mine");

        (byte[] output, string error, int code) = await RunAsync(seeds, store, ["pull"]);

        Assert.That(code, Is.Zero);
        Assert.That(ReadTar(output), Is.EqualTo(Edited));
        Assert.That(error, Does.Contain("1 page written"));
    }

    [Test]
    public async Task PullAllWritesEveryLiveSeedAsync() {
        using SeedFolder folder = new(("about", "same"), ("edited", "seed"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same");
        store.Save("/edited", "mine");

        (byte[] output, _, int code) = await RunAsync(seeds, store, ["pull", "--all"]);

        Assert.That(code, Is.Zero);
        Assert.That(ReadTar(output), Is.EqualTo(EveryLive));
    }

    [Test]
    public async Task PullMissingNamesWhatAPullCannotFixAsync() {
        using SeedFolder folder = new(("about", "same"), ("gone", "gone"), ("missing", "x"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same");
        store.Save("/gone", "gone");
        store.Delete("/gone");

        (byte[] output, string error, int code) = await RunAsync(seeds, store, ["pull", "--missing"]);

        Assert.That(code, Is.Zero);
        Assert.That(ReadTar(output), Is.Empty);
        Assert.That(error, Does.Contain("/gone deleted"));
        Assert.That(error, Does.Contain("/missing missing"));
    }

    [Test]
    public async Task AnUnchangedPullIsAValidEmptyArchiveAsync() {
        using SeedFolder folder = new(("about", "same"));
        PageSeeds seeds = new(folder.Directory);
        FakePageReadStore store = new();
        store.Save("/about", "same");

        (byte[] output, _, int code) = await RunAsync(seeds, store, ["pull"]);

        Assert.That(code, Is.Zero);
        Assert.That(ReadTar(output), Is.Empty);
        Assert.That(output.Length, Is.GreaterThanOrEqualTo(1024), "an empty stream is not a tar");
        Assert.That(output, Is.All.Zero, "an empty archive is the two end-of-archive blocks");
    }

    [Test]
    public async Task AnUnknownCommandPrintsTheUsageAsync() {
        using SeedFolder folder = new(("about", "same"));

        (_, string error, int code) = await RunAsync(new PageSeeds(folder.Directory), new FakePageReadStore(), ["harvest"]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(error, Does.Contain("usage: seeds list"));
    }

    [Test]
    public async Task AnUnknownPullOptionIsRefusedAsync() {
        using SeedFolder folder = new(("about", "same"));

        (byte[] output, string error, int code) = await RunAsync(
            new PageSeeds(folder.Directory), new FakePageReadStore(), ["pull", "--everything"]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(output, Is.Empty);
        Assert.That(error, Does.Contain("'--everything' is not a seeds pull option"));
    }

    private static async Task<(byte[] Output, string Error, int Code)> RunAsync(
        PageSeeds seeds,
        FakePageReadStore store,
        string[] args) {
        using MemoryStream output = new();
        using StringWriter error = new();

        int code = await SeedCommands.RunAsync(args, seeds, store, output, error, CancellationToken.None);

        return (output.ToArray(), error.ToString(), code);
    }

    private static List<(string Name, string Source)> ReadTar(byte[] archive) {
        using MemoryStream stream = new(archive);
        using TarReader reader = new(stream);
        List<(string Name, string Source)> entries = [];

        while (reader.GetNextEntry() is { } entry) {
            using StreamReader text = new(entry.DataStream!, Encoding.UTF8);
            entries.Add((entry.Name, text.ReadToEnd()));
        }

        return entries;
    }
}
