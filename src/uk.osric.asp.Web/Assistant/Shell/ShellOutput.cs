// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using System.Text;

/// <summary>Where an applet writes its answer.</summary>
/// <remarks>
/// An applet writes; it does not return a string. That is what will let a pipeline put the next
/// command's input where the tool result goes without an applet knowing the difference.
/// </remarks>
internal interface IShellOutput {

    /// <summary>How many matches a search may show before it has to say it stopped.</summary>
    int MatchLimit { get; }

    /// <summary>How many entries a listing may show before it has to say it stopped.</summary>
    int ListingLimit { get; }

    /// <summary>Whether the answer is missing something, because a limit stopped it.</summary>
    bool Truncated { get; }

    /// <summary>The answer, without a trailing line break.</summary>
    string Text { get; }

    /// <summary>Writes one line.</summary>
    /// <param name="text">The line, without its line break.</param>
    void Line(string text);

    /// <summary>Writes text as it stands, ending it with a line break unless it has one.</summary>
    /// <param name="text">The text.</param>
    void Raw(string text);

    /// <summary>Writes the note that says a limit was reached, and marks the answer truncated.</summary>
    /// <param name="note">The note, without its brackets.</param>
    void Stopped(string note);
}

/// <summary>An answer being written: lines in order, and whether a limit stopped it.</summary>
internal sealed class ShellOutput : IShellOutput {

    private readonly StringBuilder _text = new();

    /// <inheritdoc/>
    public int MatchLimit { get; init; } = 100;

    /// <inheritdoc/>
    public int ListingLimit { get; init; } = 500;

    /// <inheritdoc/>
    public bool Truncated { get; private set; }

    /// <inheritdoc/>
    public string Text => _text.ToString().TrimEnd('\n');

    /// <inheritdoc/>
    public void Line(string text) => _text.Append(text).Append('\n');

    /// <inheritdoc/>
    public void Raw(string text) {
        if (text.Length == 0) {
            return;
        }

        _ = _text.Append(text);
        if (!text.EndsWith('\n')) {
            _ = _text.Append('\n');
        }
    }

    /// <inheritdoc/>
    public void Stopped(string note) {
        Truncated = true;
        Line("[" + note + "]");
    }
}
