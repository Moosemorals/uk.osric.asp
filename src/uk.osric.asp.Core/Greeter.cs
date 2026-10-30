// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>Pure greeting logic: no I/O, no clock, no state.</summary>
public static class Greeter {
    public static Greeting For(string? name) {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) {
            return new Greeting("Hello, world!");
        }

        return new Greeting($"Hello, {trimmed}!");
    }
}
