// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

using System.Globalization;
using System.Text;

/// <summary>A parsed command for the page-store-only virtual shell.</summary>
/// <param name="Name">The supported command.</param>
/// <param name="Arguments">Whitespace-separated arguments after the command name.</param>
public sealed record VirtualShellCommand(string Name, IReadOnlyList<string> Arguments);

/// <summary>A parsed virtual-shell command or a safe syntax error.</summary>
/// <param name="Command">The parsed command, or null on error.</param>
/// <param name="Error">A safe syntax error, or null when valid.</param>
public sealed record VirtualShellParseResult(VirtualShellCommand? Command, string? Error);

/// <summary>Parses the restricted, read-only command language over stored page keys.</summary>
public static class VirtualShellParser {

    private static readonly HashSet<char> s_forbidden = ['|', '>', '<', ';', '&', '$', '`', '\n', '\r'];
    private static readonly HashSet<string> s_commands = ["ls", "find", "cat", "head", "tail", "wc", "stat", "grep"];

    /// <summary>Parses one command without expansion, escaping, pipes or host process execution.</summary>
    /// <param name="text">The provider-supplied command text.</param>
    public static VirtualShellParseResult Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(s_forbidden.Contains)) {
            return Invalid("Shell operators and newlines are not supported.");
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
            return Invalid("The shell command is not supported.");
        }

        string name = words[0];
        string[] arguments = [.. words.Skip(1)];
        if (!ValidArguments(name, arguments)) {
            return Invalid("The shell command has the wrong arguments.");
        }

        return new VirtualShellParseResult(new VirtualShellCommand(name, arguments), null);

        void AddToken() {
            if (!hasToken) {
                return;
            }
            words.Add(current.ToString());
            current.Clear();
            hasToken = false;
        }
    }

    private static bool ValidArguments(string command, string[] arguments) => command switch {
        "ls" or "find" => arguments.Length <= 1,
        "cat" or "stat" => arguments.Length == 1,
        "head" or "tail" => arguments.Length == 3 && arguments[0] == "-n"
            && int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0,
        "wc" => arguments.Length == 2 && arguments[0] == "-l",
        "grep" => arguments.Length is 1 or 2 && arguments[0] != "-i"
            || (arguments.Length is 2 or 3 && arguments[0] == "-i"),
        _ => false,
    };

    private static VirtualShellParseResult Invalid(string error) => new(null, error);
}
