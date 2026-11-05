// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

using System.Text;
using uk.osric.asp.Core.Text;

/// <summary>Builds a compact title from the first line of a conversation's first message.</summary>
public static class ConversationTitle {

    /// <summary>The title shown before a conversation has a user message.</summary>
    public const string NewConversation = "New conversation";

    /// <summary>The maximum title length accepted by storage and the UI.</summary>
    public const int MaximumLength = 200;

    /// <summary>Creates a whitespace-normalised title, truncated at a word boundary when possible.</summary>
    /// <param name="message">The first user message.</param>
    /// <returns>The first line, or <see cref="NewConversation"/> when that line is blank.</returns>
    public static string From(string? message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return NewConversation;
        }

        int lineEnd = message.IndexOfAny(['\r', '\n']);
        ReadOnlySpan<char> line = lineEnd < 0 ? message.AsSpan() : message.AsSpan(0, lineEnd);
        StringBuilder collapsed = new(line.Length);
        bool pendingSpace = false;

        foreach (char character in line) {
            if (char.IsWhiteSpace(character)) {
                pendingSpace = collapsed.Length > 0;
                continue;
            }

            if (pendingSpace) {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(character);
        }

        if (collapsed.Length == 0) {
            return NewConversation;
        }

        if (collapsed.Length <= MaximumLength) {
            return collapsed.ToString();
        }

        int suffixLength = MessageText.Ellipsis.Length;
        int contentLength = MaximumLength - suffixLength;
        int boundary = collapsed.ToString().AsSpan(0, contentLength).LastIndexOf(' ');
        if (boundary <= 0) {
            boundary = contentLength;
        }

        return collapsed.ToString(0, boundary).TrimEnd() + MessageText.Ellipsis;
    }
}
