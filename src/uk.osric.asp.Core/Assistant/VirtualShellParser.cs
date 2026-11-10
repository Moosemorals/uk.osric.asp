// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;
using System.Text;
using uk.osric.asp.Core.Text;

/// <summary>A parsed command for the page-store-only virtual shell.</summary>
/// <param name="Name">The supported command.</param>
/// <param name="Arguments">Whitespace-separated arguments after the command name and its options.</param>
/// <param name="Options">The options the command was given, which only it knows the meaning of.</param>
public sealed record VirtualShellCommand(
    string Name,
    IReadOnlyList<string> Arguments,
    VirtualShellOptions Options = VirtualShellOptions.None);

/// <summary>A parsed virtual-shell command or a safe syntax error.</summary>
/// <param name="Command">The parsed command, or null on error.</param>
/// <param name="Error">A safe syntax error, or null when valid.</param>
public sealed record VirtualShellParseResult(VirtualShellCommand? Command, string? Error);

/// <summary>Parses the restricted, read-only command language over stored page keys.</summary>
/// <remarks>
/// <para>
/// The whole line is lexed before any of it is judged, the way a shell lexes it: words with their
/// quoting and escaping already applied, and the first operator the line carries <em>outside</em>
/// quotes. Only an unquoted character is syntax, so <c>grep '&amp;mdash;' /notes</c> is a query and
/// <c>grep &amp;mdash; /notes</c> is not, and no rule here has to know what a particular command's
/// arguments mean in order to read the line.
/// </para>
/// <para>
/// There is no expansion, so <c>$</c> and a backtick are refused outside single quotes rather than
/// passed through as if the caller had meant them literally. There is one command per line: an
/// unquoted pipe, redirect, separator, background job or newline is refused. A first word that is
/// not one of <see cref="Commands"/> is refused as that word whatever else the line carries, because
/// "awk is not a command here" is the answer the caller can act on, and "no pipes" is not.
/// </para>
/// </remarks>
public static class VirtualShellParser {

    private static readonly HashSet<char> s_expanding = ['$', '`'];
    private static readonly HashSet<char> s_operators = ['|', '>', '<', ';', '&', '\n', '\r'];
    private static readonly HashSet<string> s_commands = ["cat", "find", "grep", "head", "help", "ls", "stat", "tail", "wc"];

    /// <summary>Every command the shell answers, in ordinal order.</summary>
    public static IReadOnlyList<string> Commands { get; } = [.. s_commands.Order(StringComparer.Ordinal)];

    /// <summary>Parses one command without pipes, expansion or host process execution.</summary>
    /// <param name="text">The provider-supplied command text.</param>
    public static VirtualShellParseResult Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);

        LexResult lexed = Lex(text);
        if (lexed.Error is not null) {
            return Invalid(lexed.Error);
        }

        if (lexed.Words.Count == 0) {
            return lexed.Operator is { } unquoted
                ? Invalid(OperatorError(unquoted))
                : Invalid("There is no command to run. Run help for the commands the shell answers.");
        }

        string name = lexed.Words[0];
        if (!s_commands.Contains(name)) {
            return Invalid($"The shell command {MessageText.Sanitize(name)} is not supported. Run help for the commands it answers.");
        }

        if (lexed.Operator is { } separator) {
            return Invalid(OperatorError(separator));
        }

        List<string> arguments = [.. lexed.Words.Skip(1)];
        VirtualShellOptions options = VirtualShellOptions.None;
        while (arguments.Count > 0 && TakeOption(name, arguments[0], ref options)) {
            arguments.RemoveAt(0);
        }

        // head, tail and wc take their option as part of their argument list; for every other command
        // a word that still starts with a dash is a guess at a host shell, not a path.
        if (name is not ("head" or "tail" or "wc") && arguments.Any(argument => argument.StartsWith('-'))) {
            return WrongArguments(name);
        }

        if (!ValidArguments(name, arguments)) {
            return WrongArguments(name);
        }

        return new VirtualShellParseResult(new VirtualShellCommand(name, arguments, options), null);
    }

    /// <summary>One command line, as a lexer sees it: words, the first operator, or the fault.</summary>
    /// <param name="Words">The words, with quoting and escaping applied.</param>
    /// <param name="Operator">The first operator outside quotes, when the line carries one.</param>
    /// <param name="Error">The fault that stopped the line, when there was one.</param>
    private sealed record LexResult(IReadOnlyList<string> Words, char? Operator, string? Error);

    /// <summary>Splits a line into words, applying quotes and escapes, and reports what stops it.</summary>
    /// <remarks>
    /// Quotes are why this is a lexer and not a scan for banned characters: a quoted character is
    /// data, so it is the only way to search for one. Adjacent parts join into one word
    /// (<c>pre"fix"</c> is <c>prefix</c>), an empty pair of quotes is an empty word, and a backslash
    /// outside single quotes takes the next character literally, as it does in a shell.
    /// </remarks>
    private static LexResult Lex(string text) {
        List<string> words = [];
        StringBuilder word = new();
        char quote = '\0';
        bool started = false;
        int index = 0;

        while (index < text.Length) {
            char character = text[index++];

            if (quote == '\'') {
                if (character == '\'') {
                    quote = '\0';
                } else {
                    _ = word.Append(character);
                }
                continue;
            }

            if (quote == '"') {
                if (character == '"') {
                    quote = '\0';
                } else if (character == '\\' && index < text.Length && text[index] is '"' or '\\' or '$' or '`') {
                    // Inside double quotes only the quote, the backslash and the characters an
                    // expansion would use are escaped, so a backslash before anything else survives
                    // into the word, as a shell leaves it.
                    _ = word.Append(text[index++]);
                } else if (s_expanding.Contains(character)) {
                    return new LexResult([], null, ExpansionError(character));
                } else {
                    _ = word.Append(character);
                }
                continue;
            }

            if (character == '\\') {
                if (index == text.Length) {
                    return new LexResult([], null,
                        "The command line ends with a backslash, which escapes nothing. Run help for the accepted form.");
                }
                _ = word.Append(text[index++]);
                started = true;
            } else if (character is '\'' or '"') {
                quote = character;
                started = true;
            } else if (s_expanding.Contains(character)) {
                return new LexResult([], null, ExpansionError(character));
            } else if (s_operators.Contains(character)) {
                return new LexResult(words, character, null);
            } else if (char.IsWhiteSpace(character)) {
                AddWord();
            } else {
                _ = word.Append(character);
                started = true;
            }
        }

        if (quote != '\0') {
            return new LexResult([], null, quote == '\''
                ? "A single-quoted word was not closed. Run help for the accepted form."
                : "A double-quoted word was not closed. Run help for the accepted form.");
        }

        AddWord();
        return new LexResult(words, null, null);

        void AddWord() {
            if (!started) {
                return;
            }
            words.Add(word.ToString());
            _ = word.Clear();
            started = false;
        }
    }

    /// <summary>Why an unquoted operator is refused.</summary>
    private static string OperatorError(char character) =>
        $"There are no pipes, redirects, separators or background jobs, so {Display(character)} is refused. Run help for the accepted form.";

    /// <summary>Why an unquoted <c>$</c> or backtick is refused.</summary>
    private static string ExpansionError(char character) =>
        $"There is no variable or command expansion, so {Display(character)} is refused; single-quote it to search for a literal one. Run help for the accepted form.";

    private static string Display(char character) => character is '\n' or '\r' ? "a newline" : $"'{character}'";

    /// <summary>Takes one leading option word, or answers false when it is not one this command has.</summary>
    /// <remarks>
    /// The table is per command because the letters mean different things in different ones: <c>-n</c>
    /// numbers lines for <c>cat</c> and <c>grep</c>, <c>-l</c> lengthens the listing for <c>ls</c>, and
    /// <c>-r</c> is a search that descends for <c>grep</c> and a reversed listing for <c>ls</c>, which
    /// this shell does not have.
    /// </remarks>
    private static bool TakeOption(string command, string word, ref VirtualShellOptions taken) {
        VirtualShellOptions option = (command, word) switch {
            ("cat", "-n") or ("grep", "-n") => VirtualShellOptions.Numbers,
            ("grep", "-i") => VirtualShellOptions.IgnoreCase,
            ("grep", "-l") => VirtualShellOptions.Names,
            ("grep", "-c") => VirtualShellOptions.Counts,
            ("grep", "-r") or ("grep", "-R") or ("ls", "-R") or ("find", "-r") or ("find", "-R") => VirtualShellOptions.Recursive,
            ("ls", "-r") => VirtualShellOptions.Reverse,
            ("ls", "-l") or ("find", "-l") => VirtualShellOptions.Long,
            _ => VirtualShellOptions.None,
        };

        if (option == VirtualShellOptions.None) {
            return false;
        }

        taken |= option;
        return true;
    }

    private static bool ValidArguments(string command, IReadOnlyList<string> arguments) => command switch {
        "find" or "help" or "ls" => arguments.Count <= 1,
        "cat" or "stat" => arguments.Count >= 1,
        "head" or "tail" => HeadOrTail(arguments),
        "wc" => Wc(arguments),
        "grep" => arguments.Count >= 1,
        _ => false,
    };

    /// <summary>The forms <c>head</c> and <c>tail</c> take: a key, a count and a key, or <c>-n</c> and both.</summary>
    /// <remarks>A lone number is a count with no key, which is a mistake rather than a key called <c>4</c>.</remarks>
    private static bool HeadOrTail(IReadOnlyList<string> arguments) => arguments.Count switch {
        1 => !IsCount(arguments[0]),
        2 => IsCount(arguments[0]),
        3 => arguments[0] == "-n" && IsCount(arguments[1]),
        _ => false,
    };

    /// <summary>The forms <c>wc</c> takes: a key, or <c>-l</c> and a key, which count the same thing.</summary>
    private static bool Wc(IReadOnlyList<string> arguments) => arguments.Count switch {
        1 => !arguments[0].StartsWith('-'),
        2 => arguments[0] == "-l",
        _ => false,
    };

    private static bool IsCount(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0;

    private static VirtualShellParseResult WrongArguments(string command) =>
        Invalid($"The shell command has the wrong arguments. Run help {command} for the form it takes.");

    private static VirtualShellParseResult Invalid(string error) => new(null, error);
}
