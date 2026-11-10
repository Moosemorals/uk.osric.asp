// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Shell;

using System.Text;

/// <summary>One token of a command line.</summary>
public abstract record ShellToken;

/// <summary>One run of a word's characters, and whether the line made them literal.</summary>
/// <param name="Text">The characters, with the quote or backslash that made them literal removed.</param>
/// <param name="IsLiteral">
/// Whether a quote or an escape stood in front of them, so that a wildcard among them is the character
/// and not a pattern.
/// </param>
public sealed record ShellSegment(string Text, bool IsLiteral);

/// <summary>A word: its characters, and which of them the line made literal.</summary>
/// <param name="Segments">The word's runs of characters, in the order they were written.</param>
public sealed record ShellWord(IReadOnlyList<ShellSegment> Segments) : ShellToken {

    /// <summary>The word as text, quoting applied.</summary>
    public string Text { get; } = string.Concat(Segments.Select(segment => segment.Text));

    /// <summary>Whether the word carries an unquoted <c>*</c> or <c>?</c>.</summary>
    public bool HasWildcard { get; } = Segments.Any(segment =>
        !segment.IsLiteral && segment.Text.AsSpan().IndexOfAny('*', '?') >= 0);
}

/// <summary>An operator between commands.</summary>
/// <param name="Character">The character: <c>|</c>, <c>&gt;</c>, <c>&lt;</c>, <c>&amp;</c>, <c>;</c>, or a line break.</param>
/// <remarks>
/// The lexer reports an operator; it does not judge it. Whether a line with one is refused, and which
/// commands may appear on either side, is the kernel's decision, so that adding pipelines is a change
/// to the kernel and not to the way a line is read.
/// </remarks>
public sealed record ShellOperator(char Character) : ShellToken;

/// <summary>A lexed command line, or the fault that stopped it.</summary>
/// <param name="Tokens">The words and operators, in the order they were written.</param>
/// <param name="Error">A safe message, or null when the line lexed.</param>
public sealed record ShellLexResult(IReadOnlyList<ShellToken> Tokens, string? Error) {

    /// <summary>The words of the line, without its operators.</summary>
    public IReadOnlyList<ShellWord> Words { get; } = [.. Tokens.OfType<ShellWord>()];

    /// <summary>The first operator the line carries, when it carries one.</summary>
    public ShellOperator? Operator { get; } = Tokens.OfType<ShellOperator>().FirstOrDefault();
}

/// <summary>Splits a command line into words and operators.</summary>
/// <remarks>
/// <para>
/// A word is a list of <see cref="ShellSegment"/>s so that quoting survives: a <c>*</c> in a quoted
/// segment is the character, and a <c>*</c> in an unquoted one is a pattern. Single quotes are literal
/// throughout, double quotes escape only <c>"</c>, <c>\</c>, <c>$</c> and a backtick, and a backslash
/// outside quotes takes the next character literally — a shell's rules, applied one character at a
/// time.
/// </para>
/// <para>
/// There is no expansion, so <c>$</c> and a backtick outside single quotes stop the line rather than
/// being passed through as data: a caller typing <c>$HOME</c> means expansion, and there is none.
/// Every fault names <c>help</c>, because a model that guessed a host-shell form should be told where
/// the accepted one is written down.
/// </para>
/// </remarks>
public static class ShellLine {

    private static readonly HashSet<char> s_expanding = ['$', '`'];
    private static readonly HashSet<char> s_operators = ['|', '>', '<', ';', '&', '\n', '\r'];

    /// <summary>Splits a command line into words and operators.</summary>
    /// <param name="text">The line as the caller wrote it.</param>
    public static ShellLexResult Lex(string text) {
        ArgumentNullException.ThrowIfNull(text);

        List<ShellToken> tokens = [];
        List<ShellSegment> word = [];
        StringBuilder run = new();
        char quote = '\0';
        bool literal = false;
        bool started = false;
        int index = 0;

        while (index < text.Length) {
            char character = text[index++];

            if (quote == '\'') {
                if (character == '\'') {
                    quote = '\0';
                } else {
                    Append(character, isLiteral: true);
                }
                continue;
            }

            if (quote == '"') {
                if (character == '"') {
                    quote = '\0';
                } else if (character == '\\' && index < text.Length && text[index] is '"' or '\\' or '$' or '`') {
                    Append(text[index++], isLiteral: true);
                } else if (s_expanding.Contains(character)) {
                    return new ShellLexResult([], ExpansionError(character));
                } else {
                    Append(character, isLiteral: true);
                }
                continue;
            }

            if (character == '\\') {
                if (index == text.Length) {
                    return new ShellLexResult([], "The command line ends with a backslash, which escapes nothing. Run help for the accepted form.");
                }
                Append(text[index++], isLiteral: true);
            } else if (character is '\'' or '"') {
                quote = character;
                started = true;
            } else if (s_expanding.Contains(character)) {
                return new ShellLexResult([], ExpansionError(character));
            } else if (s_operators.Contains(character)) {
                AddWord();
                tokens.Add(new ShellOperator(character));
            } else if (char.IsWhiteSpace(character)) {
                AddWord();
            } else {
                Append(character, isLiteral: false);
            }
        }

        if (quote != '\0') {
            return new ShellLexResult([], quote == '\''
                ? "A single-quoted word was not closed. Run help for the accepted form."
                : "A double-quoted word was not closed. Run help for the accepted form.");
        }

        AddWord();
        return new ShellLexResult(tokens, null);

        void Append(char character, bool isLiteral) {
            if (run.Length > 0 && literal != isLiteral) {
                Flush();
            }
            literal = isLiteral;
            _ = run.Append(character);
            started = true;
        }

        void Flush() {
            if (run.Length == 0) {
                return;
            }
            word.Add(new ShellSegment(run.ToString(), literal));
            _ = run.Clear();
        }

        void AddWord() {
            if (!started) {
                return;
            }
            Flush();
            tokens.Add(new ShellWord(word));
            word = [];
            started = false;
        }
    }

    /// <summary>Why an unquoted <c>$</c> or backtick stops the line.</summary>
    private static string ExpansionError(char character) =>
        $"There is no variable or command expansion, so {Display(character)} is refused; single-quote it to search for a literal one. Run help for the accepted form.";

    private static string Display(char character) => character is '\n' or '\r' ? "a newline" : $"'{character}'";
}
