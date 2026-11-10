// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>Matches page keys against the pattern language a shell expands a path with.</summary>
/// <remarks>
/// <para>
/// One segment at a time, as a shell matches a path when <c>globstar</c> is off: <c>*</c> matches any
/// run of characters inside a segment and never a <c>/</c>, <c>?</c> matches exactly one character,
/// and every other character is itself. There is no <c>**</c> and no bracket expression yet, so
/// <c>/vim/*</c> names the entries in <c>/vim</c> and nothing below them.
/// </para>
/// <para>
/// Keys are canonical — absolute, lowercase, no empty segments — so a pattern is normalised as a path
/// first and the two segment lists are compared as they are. A pattern with no wildcard matches the
/// one key it spells, which keeps expansion and plain lookups on the same path.
/// </para>
/// </remarks>
public static class PageGlob {

    /// <summary>Whether a pattern has a wildcard in it, and so needs expanding.</summary>
    /// <param name="pattern">The pattern as it was written.</param>
    public static bool IsWildcard(string pattern) {
        ArgumentNullException.ThrowIfNull(pattern);
        return pattern.AsSpan().IndexOfAny('*', '?') >= 0;
    }

    /// <summary>Whether a key matches a pattern.</summary>
    /// <param name="pattern">The pattern, as a path a shell would expand.</param>
    /// <param name="key">A canonical page key.</param>
    public static bool IsMatch(string pattern, string key) {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(key);

        string[] patternSegments = Segments(PagePath.Normalize(pattern).Key);
        string[] keySegments = Segments(PagePath.Normalize(key).Key);
        if (patternSegments.Length != keySegments.Length) {
            return false;
        }

        for (int index = 0; index < patternSegments.Length; index++) {
            if (!Segment(patternSegments[index], keySegments[index])) {
                return false;
            }
        }

        return true;
    }

    private static string[] Segments(string path) => [.. path.Split('/').Where(segment => segment.Length > 0)];

    // The classic wildcard match, with the last star remembered so a run of characters can be given
    // back when the rest of the pattern does not fit after all.
    private static bool Segment(string pattern, string text) {
        int patternIndex = 0;
        int textIndex = 0;
        int star = -1;
        int resume = 0;

        while (textIndex < text.Length) {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || pattern[patternIndex] == text[textIndex])) {
                patternIndex++;
                textIndex++;
            } else if (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
                star = patternIndex++;
                resume = textIndex;
            } else if (star >= 0) {
                patternIndex = star + 1;
                textIndex = ++resume;
            } else {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
