// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;
using System.Text;

/// <summary>A parsed command for the page-store-only virtual shell.</summary>
/// <param name="Name">The supported command.</param>
/// <param name="Arguments">Whitespace-separated arguments after the command name and its options.</param>
/// <param name="NumberLines">Whether <c>-n</c> was given, asking for numbered lines.</param>
/// <param name="IgnoreCase">Whether <c>-i</c> was given, asking for a case-insensitive match.</param>
public sealed record VirtualShellCommand(
    string Name,
    IReadOnlyList<string> Arguments,
    bool NumberLines = false,
    bool IgnoreCase = false);

/// <summary>A parsed virtual-shell command or a safe syntax error.</summary>
/// <param name="Command">The parsed command, or null on error.</param>
/// <param name="Error">A safe syntax error, or null when valid.</param>
public sealed record VirtualShellParseResult(VirtualShellCommand? Command, string? Error);

/// <summary>Parses the restricted, read-only command language over stored page keys.</summary>
/// <remarks>
/// Options are the shell's own small set rather than a general flag parser: <c>-n</c> on <c>cat</c>
/// and <c>grep</c>, <c>-i</c> on <c>grep</c>, and the <c>-n</c> and <c>-l</c> that <c>head</c>,
/// <c>tail</c> and <c>wc</c> require. The leading options are taken in any order and removed before
/// the arguments are validated, so <c>grep -n -i title /blog</c> and <c>grep -i -n title /blog</c>
/// parse alike; for those two commands a word left over that starts with a dash is a refusal rather
/// than a literal query. Every refusal names <c>help</c>, because a model that guessed a POSIX form
/// is owed the way to find the accepted one.
/// </remarks>
public static class VirtualShellParser {

    private static readonly HashSet<char> s_forbidden = ['|', '>', '<', ';', '&', '$', '`', '\n', '\r'];
    private static readonly HashSet<string> s_commands = ["cat", "find", "grep", "head", "help", "ls", "stat", "tail", "wc"];

    /// <summary>Every command the shell answers, in ordinal order.</summary>
    public static IReadOnlyList<string> Commands { get; } = [.. s_commands.Order(StringComparer.Ordinal)];

    /// <summary>Parses one command without expansion, escaping, pipes or host process execution.</summary>
    /// <param name="text">The provider-supplied command text.</param>
    public static VirtualShellParseResult Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(s_forbidden.Contains)) {
            return Invalid("Shell operators and newlines are not supported. Run help for the accepted form.");
        }

        List<string> words = [];
        StringBuilder current = new();
        char quote = '\0';
        bool hasToken = false;
        foreach (char character in text) {
            if (quote != '\0') {
                if (character == quote) {
                    quote = '\0';
                } else {
                    current.Append(character);
                }
                hasToken = true;
                continue;
            }

            if (character is '\'' or '"') {
                quote = character;
                hasToken = true;
            } else if (char.IsWhiteSpace(character)) {
                AddToken();
            } else {
                current.Append(character);
                hasToken = true;
            }
        }

        if (quote != '\0') {
            return Invalid("A quoted shell argument was not closed.");
        }
        AddToken();
        if (words.Count == 0 || !s_commands.Contains(words[0])) {
            return Invalid("The shell command is not supported. Run help for the commands it answers.");
        }

        string name = words[0];
        List<string> arguments = [.. words.Skip(1)];
        bool numberLines = false;
        bool ignoreCase = false;
        if (name is "cat" or "grep") {
            while (arguments.Count > 0 && TakeOption(name, arguments[0], ref numberLines, ref ignoreCase)) {
                arguments.RemoveAt(0);
            }

            // An option this command does not take, or one left among the arguments, is a guess at a
            // host shell rather than a query that happens to begin with a dash.
            if (arguments.Any(argument => argument.StartsWith('-'))) {
                return WrongArguments(name);
            }
        }

        if (!ValidArguments(name, arguments)) {
            return WrongArguments(name);
        }

        return new VirtualShellParseResult(new VirtualShellCommand(name, arguments, numberLines, ignoreCase), null);

        void AddToken() {
            if (!hasToken) {
                return;
            }
            words.Add(current.ToString());
            current.Clear();
            hasToken = false;
        }
    }

    /// <summary>Takes one leading option word, or answers false when it is not one this command has.</summary>
    private static bool TakeOption(string command, string word, ref bool numberLines, ref bool ignoreCase) {
        switch (word) {
            case "-n" when !numberLines:
                numberLines = true;
                return true;
            case "-i" when command == "grep" && !ignoreCase:
                ignoreCase = true;
                return true;
            default:
                return false;
        }
    }

    private static bool ValidArguments(string command, IReadOnlyList<string> arguments) => command switch {
        "find" or "help" or "ls" => arguments.Count <= 1,
        "cat" or "stat" => arguments.Count == 1,
        "head" or "tail" => arguments.Count == 3 && arguments[0] == "-n"
            && int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0,
        "wc" => arguments.Count == 2 && arguments[0] == "-l",
        "grep" => arguments.Count is 1 or 2,
        _ => false,
    };

    private static VirtualShellParseResult WrongArguments(string command) =>
        Invalid($"The shell command has the wrong arguments. Run help {command} for the form it takes.");

    private static VirtualShellParseResult Invalid(string error) => new(null, error);
}
