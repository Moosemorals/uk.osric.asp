// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>One scan of the store's sources: what it matches, where it stops and where it resumes.</summary>
[TestFixture]
public sealed class PageSourceScanTests {

    [Test]
    public async Task ALiteralIsFoundInKeyThenLineOrderWithItsColumnAndLengthAsync() {
        PageScanResult result = await Scan().RunAsync(
            Query("needle"),
            readVersion: null,
            Sources(
                new PageSourceText("/a", "nothing\nat all"),
                new PageSourceText("/b", "a needle here\nneedle again")),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Matches.Select(match => (match.Key, match.Line, match.Column, match.Length)), Is.EqualTo([
                ("/b", 1, 3, 6),
                ("/b", 2, 1, 6),
            ]));
            Assert.That(result.Matches[0].Text, Is.EqualTo("a needle here"), "the whole line, which the caller cuts");
            Assert.That(result.End, Is.EqualTo(PageSearchEnd.Exhausted));
            Assert.That(result.Cursor, Is.Null);
        });
    }

    [Test]
    public async Task OneLineIsOneRowHoweverManyTimesItMatchesAsync() {
        PageScanResult result = await Scan().RunAsync(
            Query("needle"),
            readVersion: null,
            Sources(new PageSourceText("/a", "needle and needle")),
            CancellationToken.None);

        Assert.That(result.Matches, Has.Count.EqualTo(1));
        Assert.That(result.Matches[0].Column, Is.EqualTo(1));
    }

    [Test]
    public async Task ALimitStopsOnTheNextMatchAndTheCursorNamesItAsync() {
        PageScanResult result = await Scan().RunAsync(
            Query("needle", limit: 2),
            readVersion: null,
            Sources(new PageSourceText("/a", "needle\nneedle\nneedle")),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(result.End, Is.EqualTo(PageSearchEnd.Limit));
            Assert.That(result.Cursor, Is.EqualTo(new PageSearchCursor("/a", 3)), "the third match is not lost");
        });
    }

    [Test]
    public async Task ACursorResumesInsideTheSourceItNamesAsync() {
        PageScanResult result = await Scan().RunAsync(
            Query("needle", after: new PageSearchCursor("/b", 3)),
            readVersion: null,
            Sources(
                new PageSourceText("/a", "needle"),
                new PageSourceText("/b", "needle\nneedle\nneedle\nneedle")),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Matches.Select(match => (match.Key, match.Line)), Is.EqualTo([
                ("/b", 3),
                ("/b", 4),
            ]));
            Assert.That(result.Cursor, Is.Null);
        });
    }

    [Test]
    public async Task ASliceThatFindsNothingStillLeavesTheNextPositionAsync() {
        PageScanResult result = await Scan().RunAsync(
            Query("absent", limit: 1),
            readVersion: null,
            Sources(new PageSourceText("/a", "one\ntwo")),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.End, Is.EqualTo(PageSearchEnd.Exhausted));
            Assert.That(result.Cursor, Is.Null, "the scope ran out rather than stopping short");
        });
    }

    [Test]
    public async Task TheAllowanceStopsTheScanAndTheCursorNamesTheUntestedLineAsync() {
        TestClock clock = new();
        PageSourceScan scan = Scan(TestOptions.Search with { MaxDurationMs = 30 }, clock);

        PageScanResult result = await scan.RunAsync(
            Query("needle"),
            readVersion: null,
            (_, token) => TwoSourcesTooSlowAsync(clock, token),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Matches.Select(match => match.Key), Is.EqualTo(["/a"]));
            Assert.That(result.End, Is.EqualTo(PageSearchEnd.Duration));
            Assert.That(result.Cursor, Is.EqualTo(new PageSearchCursor("/b", 1)), "the line the scan did not reach");
        });
    }

    [Test]
    public async Task AChangedTokenDiscardsTheCursorAndStartsAgainAsync() {
        PageSearchCursor? asked = new PageSearchCursor("/a", 2);

        PageScanResult result = await Scan().RunAsync(
            Query("needle", after: asked, version: "before"),
            _ => Task.FromResult("after"),
            (cursor, _) => {
                asked = cursor;
                return FromAsync(new PageSourceText("/a", "needle\nneedle"));
            },
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(asked, Is.Null, "a token that does not match is not trusted");
            Assert.That(result.Changed, Is.True);
            Assert.That(result.Version, Is.EqualTo("after"));
            Assert.That(result.Matches, Has.Count.EqualTo(2), "the walk started at the beginning");
        });
    }

    [Test]
    public async Task AMatchingTokenKeepsTheCursorAsync() {
        PageSearchCursor? asked = new PageSearchCursor("/a", 2);

        PageScanResult result = await Scan().RunAsync(
            Query("needle", after: new PageSearchCursor("/a", 2), version: "same"),
            _ => Task.FromResult("same"),
            (cursor, _) => {
                asked = cursor;
                return FromAsync(new PageSourceText("/a", "needle\nneedle"));
            },
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(asked, Is.EqualTo(new PageSearchCursor("/a", 2)));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Matches, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ALimitBelowOneIsRefusedAsync() {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Scan().RunAsync(Query("needle", limit: 0), null, (_, _) => FromAsync(), CancellationToken.None));
    }

    private static PageSourceScan Scan(PageSearchOptions? options = null, TimeProvider? clock = null) =>
        new(options ?? TestOptions.Search, clock ?? TimeProvider.System, new PageMetrics());

    private static PageScanQuery Query(
        string pattern,
        int limit = 100,
        PageSearchCursor? after = null,
        string? version = null) {
        Assert.That(
            GrepPattern.TryCompile(pattern, literal: true, ignoreCase: false, out GrepPattern? compiled, out string? error),
            Is.True, error);

        return new PageScanQuery(compiled!, limit, after, version, TemplateLines.Split, PageSearchCaller.Page);
    }

    private static Func<PageSearchCursor?, CancellationToken, IAsyncEnumerable<PageSourceText>> Sources(
        params PageSourceText[] sources) => (cursor, _) => FromAsync(
            [.. sources.Where(source => cursor is null || string.CompareOrdinal(source.Key, cursor.Value.Key) >= 0)]);

    private static async IAsyncEnumerable<PageSourceText> FromAsync(params PageSourceText[] sources) {
        foreach (PageSourceText source in sources) {
            await Task.Yield();
            yield return source;
        }
    }

    private static async IAsyncEnumerable<PageSourceText> TwoSourcesTooSlowAsync(
        TestClock clock, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new PageSourceText("/a", "needle");

        // The first source has been read; the allowance has run out by the time the second is reached.
        clock.Advance(TimeSpan.FromMilliseconds(40));
        await Task.Yield();
        yield return new PageSourceText("/b", "needle");
    }
}
