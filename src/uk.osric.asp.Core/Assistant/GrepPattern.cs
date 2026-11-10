// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>A grep query, as the matcher that runs it.</summary>
/// <remarks>
/// <para>
/// The language is the basic regular expression <c>grep</c> has always had, because that is what a
/// caller reaching for <c>grep</c> writes: <c>^</c>, <c>$</c>, <c>.</c>, <c>*</c> and <c>[...]</c> are
/// metacharacters, <c>\|</c> is alternation, and everything else — <c>(</c>, <c>)</c>, <c>{</c>,
/// <c>}</c>, <c>+</c>, <c>?</c> and a bare <c>|</c> included — is the literal character. That last
/// part is the one that matters most: a page key or a snippet of HTML is full of punctuation that an
/// extended regular expression would read as syntax, so a query means what it looks like until it
/// says otherwise.
/// </para>
/// <para>
/// A query with no metacharacters at all is matched as a literal substring, which is both the common
/// case and the cheap one. Anything else is translated to the form <see cref="Regex"/> takes and
/// compiled with <see cref="RegexOptions.NonBacktracking"/>: matching then costs at most linear time
/// in the line, so a pattern cannot be used to burn a run's budget and no timer is needed to stop it.
/// A construct that engine does not carry — a backreference, a lookaround — is refused rather than
/// quietly matched as something else.
/// </para>
/// </remarks>
public sealed class GrepPattern {

    private readonly StringComparison _comparison;
    private readonly string? _literal;
    private readonly Regex? _expression;

    private GrepPattern(string? literal, StringComparison comparison, Regex? expression) {
        _literal = literal;
        _comparison = comparison;
        _expression = expression;
    }

    /// <summary>Compiles a query, or explains why it cannot be one.</summary>
    /// <param name="query">The query as the caller wrote it.</param>
    /// <param name="ignoreCase">Whether matching ignores case.</param>
    /// <param name="pattern">The matcher, or null when the query was refused.</param>
    /// <param name="error">Why the query was refused, or null when it compiled.</param>
    public static bool TryCompile(string query, bool ignoreCase, out GrepPattern? pattern, out string? error) {
        ArgumentNullException.ThrowIfNull(query);

        pattern = null;
        error = null;
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (query.AsSpan().IndexOfAny("^$.*[]\\") < 0) {
            pattern = new GrepPattern(query, comparison, null);
            return true;
        }

        if (!TryTranslate(query, out string translated, out error)) {
            return false;
        }

        RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if (ignoreCase) {
            options |= RegexOptions.IgnoreCase;
        }

        try {
            pattern = new GrepPattern(null, comparison, new Regex(translated, options));
            return true;
        } catch (ArgumentException) {
            error = "The query is not a pattern this shell can read. Run help grep for the syntax it takes.";
            return false;
        } catch (NotSupportedException) {
            error = "The query uses a regular expression feature this shell does not support yet. Run help grep for the syntax it takes.";
            return false;
        }
    }

    /// <summary>Whether a line of page source matches.</summary>
    /// <param name="line">One line, without its line break.</param>
    public bool IsMatch(string line) => _literal is not null
        ? line.Contains(_literal, _comparison)
        : _expression!.IsMatch(line);

    /// <summary>Rewrites a basic regular expression into the form <see cref="Regex"/> reads.</summary>
    /// <remarks>
    /// The whole of the difference is which characters are syntax: <c>|</c>, <c>(</c>, <c>)</c>,
    /// <c>{</c>, <c>}</c>, <c>+</c> and <c>?</c> are literal here and are escaped on the way out,
    /// <c>\|</c> becomes an alternation, and the class in <c>[...]</c> is copied as it stands because
    /// the two languages agree inside one. <c>^</c> and <c>$</c> anchor only at the ends of the query,
    /// which is where a basic expression anchors them: in the middle they are the character, so a
    /// search for <c>$5</c> or <c>x^2</c> means what it looks like.
    /// </remarks>
    private static bool TryTranslate(string query, out string translated, out string? error) {
        StringBuilder text = new(query.Length + 8);
        error = null;
        translated = "";
        int index = 0;

        while (index < query.Length) {
            char character = query[index++];
            switch (character) {
                case '\\':
                    if (index == query.Length) {
                        error = "The query ends with a backslash, which escapes nothing. Run help grep for the syntax it takes.";
                        return false;
                    }
                    char escaped = query[index++];
                    if (escaped == '|') {
                        _ = text.Append('|');
                    } else if (escaped is '(' or ')' or '{' or '}' or '+' or '?') {
                        _ = text.Append(Regex.Escape(escaped.ToString()));
                    } else {
                        _ = text.Append('\\').Append(escaped);
                    }
                    break;

                case '(' or ')' or '{' or '}' or '+' or '?' or '|':
                    _ = text.Append(Regex.Escape(character.ToString()));
                    break;

                case '[':
                    if (!CopyClass(query, ref index, text)) {
                        error = "The query has a character class that is not closed. Run help grep for the syntax it takes.";
                        return false;
                    }
                    break;

                case '^':
                    // An anchor only where a basic expression anchors: at the start of the query.
                    // Anywhere else it is the character, so a query for "x^2" searches for it.
                    _ = text.Append(index == 1 ? "^" : "\\^");
                    break;

                case '$':
                    _ = text.Append(index == query.Length ? "$" : "\\$");
                    break;

                default:
                    _ = text.Append(character);
                    break;
            }
        }

        translated = text.ToString();
        return true;
    }

    /// <summary>Copies a character class, from its opening bracket to its closing one.</summary>
    private static bool CopyClass(string query, ref int index, StringBuilder text) {
        _ = text.Append('[');
        if (index < query.Length && query[index] == '^') {
            _ = text.Append(query[index++]);
        }
        if (index < query.Length && query[index] == ']') {
            _ = text.Append(query[index++]);
        }

        while (index < query.Length) {
            char member = query[index++];
            if (member == ']') {
                _ = text.Append(member);
                return true;
            }
            if (member == '\\' && index < query.Length) {
                _ = text.Append(member).Append(query[index++]);
                continue;
            }
            _ = text.Append(member);
        }

        return false;
    }
}
