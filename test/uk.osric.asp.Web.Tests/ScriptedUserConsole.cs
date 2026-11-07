// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Users;

/// <summary>A console whose answers and output are visible to the test.</summary>
internal sealed class ScriptedUserConsole : IUserConsole {

    private readonly Queue<string?> _passwords = new();

    public List<string> Output { get; } = [];

    public List<string> Errors { get; } = [];

    public void AddPassword(string? password) {
        _passwords.Enqueue(password);
    }

    public string? ReadPassword(string prompt) {
        return _passwords.Count > 0 ? _passwords.Dequeue() : null;
    }

    public void WriteLine(string message) {
        Output.Add(message);
    }

    public void WriteError(string message) {
        Errors.Add(message);
    }
}
