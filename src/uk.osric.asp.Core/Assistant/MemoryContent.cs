// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The text of a memory, normalised, or the reason it cannot be stored.</summary>
/// <param name="Content">The normalised text, or the empty string when the content was refused.</param>
/// <param name="Error">A sentence the model can act on, or null when the content is usable.</param>
public sealed record MemoryContentResult(string Content, string? Error) {

    /// <summary>Whether the content may be stored.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>A refusal the caller can hand straight back as a tool result.</summary>
    /// <param name="error">Why the content was refused.</param>
    /// <returns>The result.</returns>
    public static MemoryContentResult Refused(string error) => new(string.Empty, error);
}

/// <summary>Normalises and validates the text of a memory before it is stored.</summary>
/// <remarks>
/// A memory is one note, not a document: its whitespace is collapsed so the same note written twice
/// compares equal, and one that is too long is refused rather than truncated, because a truncated
/// memory is a sentence the model never wrote.
/// </remarks>
public static class MemoryContent {

    /// <summary>Trims, collapses whitespace and checks the length.</summary>
    /// <param name="content">The model-supplied text.</param>
    /// <param name="maximumChars">The configured length cap, in characters.</param>
    /// <returns>The normalised text, or the refusal.</returns>
    public static MemoryContentResult Normalise(string? content, int maximumChars) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        if (content is null) {
            return MemoryContentResult.Refused("A memory needs some content.");
        }

        if (content.Any(character => char.IsControl(character) && character is not ('\t' or '\n' or '\r'))) {
            return MemoryContentResult.Refused("A memory cannot contain control characters.");
        }

        string normalised = Collapse(content);
        if (normalised.Length == 0) {
            return MemoryContentResult.Refused("A memory needs some content.");
        }

        return normalised.Length > maximumChars
            ? MemoryContentResult.Refused($"A memory is at most {maximumChars.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters. Store the short version, or write a page.")
            : new MemoryContentResult(normalised, null);
    }

    /// <summary>Whether two normalised memories are the same note.</summary>
    /// <param name="left">One normalised memory.</param>
    /// <param name="right">Another normalised memory.</param>
    /// <returns>Whether they differ only in case.</returns>
    public static bool SameNote(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    // Whitespace, not just spaces: a memory written across three lines and one written on one compare
    // equal, so the duplicate rule sees the note rather than its layout.
    private static string Collapse(string content) {
        System.Text.StringBuilder builder = new(content.Length);
        bool pendingSpace = false;
        foreach (char character in content) {
            if (char.IsWhiteSpace(character)) {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace) {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
