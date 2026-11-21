// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>One pattern language, as the matcher that runs it for a page, a tool and the shell.</summary>
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
/// <para>
/// A caller that names the kind gets that kind: a literal query is the characters themselves even
/// when it is full of punctuation, and a basic expression is a pattern even when it holds none. The
/// refusal is the reason alone; each caller adds the sentence that says where its syntax is taught.
/// </para>
/// </remarks>
public sealed class GrepPattern {

    // The characters that make a query a pattern rather than a literal substring: if none is present
    // the cheap substring path is the whole match.
    private static readonly SearchValues<char> s_metacharacters = SearchValues.Create("^$.*[]\\");

    private readonly StringComparison _comparison;
    private readonly string? _literal;
    private readonly Regex? _expression;

    private GrepPattern(string? literal, StringComparison comparison, Regex? expression) {
        _literal = literal;
        _comparison = comparison;
        _expression = expression;
    }

    /// <summary>Compiles a query, reading it as the language reads it.</summary>
    /// <param name="query">The query as the caller wrote it.</param>
    /// <param name="ignoreCase">Whether matching ignores case.</param>
    /// <param name="pattern">The matcher, or null when the query was refused.</param>
    /// <param name="error">Why the query was refused, or null when it compiled.</param>
    /// <remarks>
    /// A query with no metacharacter is a literal substring; anything else is a basic expression. This
    /// is the form <c>grep</c> keeps while the shell is rewritten; a caller that knows which it means
    /// names the kind instead.
    /// </remarks>
    public static bool TryCompile(string query, bool ignoreCase, out GrepPattern? pattern, out string? error) {
        ArgumentNullException.ThrowIfNull(query);

        return TryCompile(
            query,
            literal: query.AsSpan().IndexOfAny(s_metacharacters) < 0,
            ignoreCase,
            out pattern,
            out error);
    }

    /// <summary>Compiles a query as the kind the caller named, or explains why it cannot be one.</summary>
    /// <param name="query">The query as the caller wrote it.</param>
    /// <param name="literal">
    /// Whether the query is the characters themselves. A literal query is never a pattern, however
    /// much punctuation it holds; a basic expression is a pattern even when it holds none.
    /// </param>
    /// <param name="ignoreCase">Whether matching ignores case.</param>
    /// <param name="pattern">The matcher, or null when the query was refused.</param>
    /// <param name="error">Why the query was refused, or null when it compiled.</param>
    public static bool TryCompile(
        string query, bool literal, bool ignoreCase, out GrepPattern? pattern, out string? error) {
        ArgumentNullException.ThrowIfNull(query);

        pattern = null;
        error = null;
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // A basic expression with no metacharacter means the same as the text it spells, so the cheap
        // path is the same answer rather than a second language.
        if (literal || query.AsSpan().IndexOfAny(s_metacharacters) < 0) {
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
            error = "The query is not a pattern this language can read.";
            return false;
        } catch (NotSupportedException) {
            error = "The query uses a regular expression feature this language does not support yet.";
            return false;
        }
    }

    /// <summary>Whether a line of page source matches.</summary>
    /// <param name="line">One line, without its line break.</param>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    public bool IsMatch(string line) => TryMatch(line, out _, out _);

    /// <summary>Where a line of page source first matches.</summary>
    /// <param name="line">One line, without its line break.</param>
    /// <param name="index">The 0-based offset of the match's first character, or zero when none.</param>
    /// <param name="length">The match's length, or zero when none.</param>
    /// <returns>Whether the line matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A pattern that can match nothing — <c>a*</c> — answers a zero-length match at the first place it
    /// fits, which is what POSIX matching does.
    /// </remarks>
    public bool TryMatch(string line, out int index, out int length) {
        ArgumentNullException.ThrowIfNull(line);

        if (_literal is not null) {
            int at = line.IndexOf(_literal, _comparison);
            index = at < 0 ? 0 : at;
            length = at < 0 ? 0 : _literal.Length;
            return at >= 0;
        }

        Match match = _expression!.Match(line);
        index = match.Success ? match.Index : 0;
        length = match.Success ? match.Length : 0;
        return match.Success;
    }

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
                        error = "The query ends with a backslash, which escapes nothing.";
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
                        error = "The query has a character class that is not closed.";
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
