// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>Who asked for a scan, for the one metric that distinguishes them.</summary>
internal enum PageSearchCaller {

    /// <summary>A page's <see cref="IPageCatalog.SearchAsync"/>.</summary>
    Page,

    /// <summary>The virtual shell, once its rewrite uses the scan.</summary>
    Shell,
}

/// <summary>Why a scan stopped.</summary>
internal enum PageSearchEnd {

    /// <summary>The scope ran out, so the answer is the whole of it.</summary>
    Exhausted,

    /// <summary>The caller's limit was reached, and the position of the next match is the cursor.</summary>
    Limit,

    /// <summary>The time allowance expired before the scope ran out.</summary>
    Duration,
}

/// <summary>One source as the scan reads it: a key and the text stored there.</summary>
/// <param name="Key">The canonical key.</param>
/// <param name="Text">The source: a live page's template, or the shell's record of one of its routes.</param>
internal sealed record PageSourceText(string Key, string Text);

/// <summary>One matching line, as the scan answers it.</summary>
/// <param name="Key">The key the line was read from.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column the match starts at.</param>
/// <param name="Length">The match's length, which a snippet is never narrower than.</param>
/// <param name="Text">The whole line, without its break; a caller cuts the snippet it wants.</param>
internal sealed record PageScanMatch(string Key, int Line, int Column, int Length, string Text);

/// <summary>What one scan found, where it stopped and what it read.</summary>
/// <param name="Matches">The matching lines, in key then line order.</param>
/// <param name="Cursor">The first position the answer did not include, or null at the end of the scope.</param>
/// <param name="End">Why the scan stopped.</param>
/// <param name="Version">The prefix's fingerprint, or empty when the caller cannot resume.</param>
/// <param name="Changed">Whether the request's token differed from the fingerprint just read.</param>
internal sealed record PageScanResult(
    IReadOnlyList<PageScanMatch> Matches,
    PageSearchCursor? Cursor,
    PageSearchEnd End,
    string Version,
    bool Changed);

/// <summary>One question, as the scan reads it: the compiled pattern, the slice, the line rule and who asked.</summary>
/// <param name="Pattern">The compiled matcher.</param>
/// <param name="Limit">The most matches the answer may carry.</param>
/// <param name="After">The cursor to resume from, or null for the first slice.</param>
/// <param name="Version">The token the request carried, or null for a first call.</param>
/// <param name="Lines">The caller's line rule.</param>
/// <param name="Caller">Who asked, for the search's two instruments.</param>
internal sealed record PageScanQuery(
    GrepPattern Pattern,
    int Limit,
    PageSearchCursor? After,
    string? Version,
    Func<string, string[]> Lines,
    PageSearchCaller Caller);

/// <summary>One scan of the store's sources, shared by every front-end that asks a line-shaped question.</summary>
/// <remarks>
/// <para>
/// The walk is one loop: sources in key order, each split by the caller's own line rule, each line tested
/// by the caller's own pattern, one row per matching line with the match's column and length. Only the
/// projection and the wording stay with the caller, so a page, a tool and the shell cannot disagree
/// about what matches.
/// </para>
/// <para>
/// The allowance is measured in time and covers the whole call — the fingerprint, the stepped reads and
/// the matching — because a count bounds the answer while the work is done whether anything matches.
/// A scan that runs out stops and says where to resume; it never trims an answer it has already
/// promised.
/// </para>
/// <para>
/// The two line rules are deliberately not one. <c>TemplateLines.Split</c> counts physical template
/// lines, which are the lines <c>read_page</c> and the editor name; <c>ShellText.Split</c> counts
/// terminated lines, as POSIX does. Merging them would make an edit address the wrong line in a page
/// whose source does not end with a newline.
/// </para>
/// </remarks>
internal sealed class PageSourceScan(PageSearchOptions options, TimeProvider clock, PageMetrics metrics) {

    /// <summary>The values this scan runs with.</summary>
    public PageSearchOptions Options { get; } = options;

    /// <summary>Scans the sources for matching lines.</summary>
    /// <param name="query">The compiled pattern, the slice and the line rule.</param>
    /// <param name="readVersion">
    /// The prefix's fingerprint, read under the allowance, or null for a caller that cannot resume and so
    /// never carries a token.
    /// </param>
    /// <param name="sources">
    /// The sources to read, already in key order. It is asked for the position to start at, which is the
    /// cursor's key and is inclusive, so a walk can resume inside one source.
    /// </param>
    /// <param name="cancellationToken">The render's or the run's token.</param>
    /// <returns>The matches found before a stop, where to resume and whether the store moved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The limit is not positive.</exception>
    public async Task<PageScanResult> RunAsync(
        PageScanQuery query,
        Func<CancellationToken, Task<string>>? readVersion,
        Func<PageSearchCursor?, CancellationToken, IAsyncEnumerable<PageSourceText>> sources,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);

        long start = clock.GetTimestamp();

        // The fingerprint is read first, under the same allowance, because a token the caller carried
        // has to be judged before the walk begins. A token that does not match is not trusted: a search
        // reads text, so a store that moved can hide a match rather than merely move a row.
        string version = readVersion is null ? string.Empty : await readVersion(cancellationToken);
        bool changed = query.Version is not null;
        changed &= !string.Equals(query.Version, version, StringComparison.Ordinal);
        PageSearchCursor? cursor = changed ? null : query.After;

        List<PageScanMatch> matches = [];

        await foreach (PageSourceText source in sources(cursor, cancellationToken)) {
            string[] lines = query.Lines(source.Text);
            int first = cursor is { } at && string.Equals(at.Key, source.Key, StringComparison.Ordinal)
                ? Math.Max(1, at.Line)
                : 1;

            for (int line = first; line <= lines.Length; line++) {
                PageSearchCursor here = new(source.Key, line);

                if (clock.GetElapsedTime(start) >= Options.Duration) {
                    return Stop(PageSearchEnd.Duration, here);
                }

                if (!query.Pattern.TryMatch(lines[line - 1], out int index, out int length)) {
                    continue;
                }

                // The answer stops one match past the limit, and the cursor names that match: stopping
                // before it would lose it, and guessing from the count would not be an observation.
                if (matches.Count == query.Limit) {
                    return Stop(PageSearchEnd.Limit, here);
                }

                matches.Add(new PageScanMatch(source.Key, line, index + 1, length, lines[line - 1]));
            }
        }

        return Stop(PageSearchEnd.Exhausted, null);

        PageScanResult Stop(PageSearchEnd end, PageSearchCursor? next) {
            metrics.RecordSearch(
                query.Caller == PageSearchCaller.Page ? PageMetrics.SearchCallerPage : PageMetrics.SearchCallerShell,
                end == PageSearchEnd.Duration,
                clock.GetElapsedTime(start));

            return new PageScanResult(matches, next, end, version, changed);
        }
    }
}
