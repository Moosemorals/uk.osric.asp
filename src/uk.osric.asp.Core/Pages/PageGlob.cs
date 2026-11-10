// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using uk.osric.asp.Core.Shell;

/// <summary>Matches page keys against the pattern language a shell expands a path with.</summary>
/// <remarks>
/// <para>
/// One segment at a time, as a shell matches a path when <c>globstar</c> is off: <c>*</c> matches any
/// run of characters inside a segment and never a <c>/</c>, <c>?</c> matches exactly one character,
/// and every other character is itself. There is no <c>**</c> and no bracket expression yet, so
/// <c>/vim/*</c> names the entries in <c>/vim</c> and nothing below them.
/// </para>
/// <para>
/// A pattern is matched as a <see cref="ShellWord"/>, so a wildcard the caller quoted is the
/// character: <c>'/vim/*.js'</c> names the one key with a star in it, and <c>/vim/*.js</c> names every
/// key it fits. Keys are canonical — absolute, lowercase, no empty segments — so a pattern is
/// normalised as a path first and the two segment lists are compared as they are.
/// </para>
/// </remarks>
public static class PageGlob {

    /// <summary>Whether a key matches a pattern written as a plain string.</summary>
    /// <param name="pattern">The pattern, as a path a shell would expand.</param>
    /// <param name="key">A canonical page key.</param>
    public static bool IsMatch(string pattern, string key) {
        ArgumentNullException.ThrowIfNull(pattern);

        string normalised = PagePath.Normalize(pattern).Key;
        return IsMatch([.. normalised.Select(character => (character, character is '*' or '?'))], key);
    }

    /// <summary>Whether a key matches a word, which knows which of its characters were quoted.</summary>
    /// <param name="pattern">The word, as the caller wrote it.</param>
    /// <param name="key">A canonical page key.</param>
    public static bool IsMatch(ShellWord pattern, string key) {
        ArgumentNullException.ThrowIfNull(pattern);

        // A key is lowercase, so a word is read that way too; the rest of it is used as written,
        // because a word already knows which of its characters were quoted.
        return IsMatch(
            [.. pattern.Segments.SelectMany(segment =>
                segment.Text.ToLowerInvariant().Select(character =>
                    (character, !segment.IsLiteral && character is '*' or '?')))],
            key);
    }

    private static bool IsMatch(IReadOnlyList<(char Letter, bool Wild)> pattern, string key) {
        ArgumentNullException.ThrowIfNull(key);

        List<List<(char Letter, bool Wild)>> patternSegments = Segments(pattern);
        string[] keySegments = Segments(PagePath.Normalize(key).Key);
        if (patternSegments.Count != keySegments.Length) {
            return false;
        }

        for (int index = 0; index < patternSegments.Count; index++) {
            if (!Segment(patternSegments[index], keySegments[index])) {
                return false;
            }
        }

        return true;
    }

    private static List<List<(char Letter, bool Wild)>> Segments(IReadOnlyList<(char Letter, bool Wild)> pattern) {
        List<List<(char Letter, bool Wild)>> segments = [];
        List<(char Letter, bool Wild)> current = [];
        foreach ((char letter, bool wild) in pattern) {
            if (letter == '/') {
                if (current.Count > 0) {
                    segments.Add(current);
                    current = [];
                }
            } else {
                current.Add((letter, wild));
            }
        }
        if (current.Count > 0) {
            segments.Add(current);
        }

        return segments;
    }

    private static string[] Segments(string path) => [.. path.Split('/').Where(segment => segment.Length > 0)];

    // The classic wildcard match, over a segment whose pattern characters know whether they are
    // wildcards, with the last star remembered so a run of characters can be given back when the rest
    // of the pattern does not fit after all.
    private static bool Segment(IReadOnlyList<(char Letter, bool Wild)> pattern, string text) {
        int patternIndex = 0;
        int textIndex = 0;
        int star = -1;
        int resume = 0;

        while (textIndex < text.Length) {
            if (patternIndex < pattern.Count) {
                (char letter, bool wild) = pattern[patternIndex];
                if (wild && letter == '?') {
                    patternIndex++;
                    textIndex++;
                    continue;
                }
                if (letter == text[textIndex]) {
                    patternIndex++;
                    textIndex++;
                    continue;
                }
                if (wild && letter == '*') {
                    star = patternIndex++;
                    resume = textIndex;
                    continue;
                }
            }

            if (star < 0) {
                return false;
            }
            patternIndex = star + 1;
            textIndex = ++resume;
        }

        while (patternIndex < pattern.Count && pattern[patternIndex].Wild && pattern[patternIndex].Letter == '*') {
            patternIndex++;
        }

        return patternIndex == pattern.Count;
    }
}
