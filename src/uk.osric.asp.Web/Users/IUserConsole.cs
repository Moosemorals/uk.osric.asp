// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

/// <summary>The console operations the user command needs, so its logic can be tested.</summary>
internal interface IUserConsole {

    /// <summary>Writes a prompt and reads a line.</summary>
    string? ReadLine(string prompt);

    /// <summary>Writes a prompt and reads a password without echoing it.</summary>
    string? ReadPassword(string prompt);

    /// <summary>Writes an informational line.</summary>
    void WriteLine(string message);

    /// <summary>Writes an error line.</summary>
    void WriteError(string message);
}
