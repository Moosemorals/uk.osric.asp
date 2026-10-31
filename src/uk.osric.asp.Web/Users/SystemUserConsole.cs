// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using System.Text;

/// <summary>The real console, including a password prompt that does not echo.</summary>
internal sealed class SystemUserConsole : IUserConsole {

    /// <inheritdoc/>
    public string? ReadLine(string prompt) {
        Console.Write(prompt);
        return Console.ReadLine();
    }

    /// <inheritdoc/>
    public string? ReadPassword(string prompt) {
        // Piped input is a script, not a person: no prompt, and one line is the whole password.
        if (Console.IsInputRedirected) {
            return Console.ReadLine();
        }

        Console.Write(prompt);
        StringBuilder password = new();
        while (true) {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter) {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace) {
                if (password.Length > 0) {
                    password.Length--;
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar)) {
                password.Append(key.KeyChar);
            }
        }
    }

    /// <inheritdoc/>
    public void WriteLine(string message) {
        Console.WriteLine(message);
    }

    /// <inheritdoc/>
    public void WriteError(string message) {
        Console.Error.WriteLine(message);
    }
}
