// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>The generated C# source of a page's handler: the form between a template and an assembly.</summary>
public sealed record GeneratedSource {

    /// <summary>Wraps generated source.</summary>
    /// <param name="text">The C# the template generator produced.</param>
    /// <exception cref="ArgumentException">The text is empty.</exception>
    internal GeneratedSource(string text) {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Text = text;
    }

    /// <summary>The generated C# source, ready for the compiler.</summary>
    public string Text { get; }
}
