// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Pages;

/// <summary>One line of a page that a search matched.</summary>
/// <param name="Path">The key the line was read from.</param>
/// <param name="Line">The 1-based line of that key.</param>
/// <param name="Text">The line itself, without its line break.</param>
internal readonly record struct LineMatch(PagePath Path, int Line, string Text);

/// <summary>What a scan found, and whether a limit left something out.</summary>
/// <param name="Matches">The matching lines, in key then line order.</param>
/// <param name="Truncated">Whether a limit stopped the scan before the sources ran out.</param>
internal sealed record LineMatches(IReadOnlyList<LineMatch> Matches, bool Truncated);

/// <summary>One scan of page sources, shared by the search tool and the shell's <c>grep</c>.</summary>
/// <remarks>
/// <para>
/// The walk is the same in both: sources in key order, each split into lines by the caller's own rule,
/// each line tested by the caller's own predicate. Only the projection and the wording differ, and they
/// stay with the caller.
/// </para>
/// <para>
/// The two line rules are deliberately not one. <c>PageToolData.TemplateSourceLines</c> counts physical
/// template lines, which are the lines <c>read_page</c> and <c>edit_page</c> name; <c>ShellText.Split</c>
/// counts terminated lines, as POSIX does. Merging them would make an edit address the wrong line in a
/// page whose source does not end with a newline.
/// </para>
/// </remarks>
internal static class PageSourceSearch {

    /// <summary>Scans the sources for matching lines.</summary>
    /// <param name="sources">The sources to read, already in key order.</param>
    /// <param name="lines">The caller's line rule for a source's text.</param>
    /// <param name="matches">The caller's test for one line.</param>
    /// <param name="limit">How many matches the answer may carry.</param>
    /// <returns>
    /// The matches found before the limit, and whether a further match existed. The scan stops one match
    /// past the limit rather than guessing from the count, so "there are more" is an observation.
    /// </returns>
    public static LineMatches Find(
        IReadOnlyList<PageRead> sources,
        Func<string, string[]> lines,
        Func<string, bool> matches,
        int limit) {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<LineMatch> found = [];
        foreach (PageRead source in sources) {
            string[] text = lines(source.Text);
            for (int index = 0; index < text.Length; index++) {
                if (!matches(text[index])) {
                    continue;
                }
                if (found.Count == limit) {
                    return new LineMatches(found, Truncated: true);
                }
                found.Add(new LineMatch(source.Path, index + 1, text[index]));
            }
        }

        return new LineMatches(found, Truncated: false);
    }
}
