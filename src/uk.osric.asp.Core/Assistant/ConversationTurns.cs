// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>A transcript split into its system prompt and whole conversation turns.</summary>
/// <param name="System">The system prompt when the transcript starts with one.</param>
/// <param name="Turns">Whole turns, oldest first, each beginning at a user message.</param>
internal sealed record ConversationSplit(
    ChatMessage? System,
    IReadOnlyList<IReadOnlyList<ChatMessage>> Turns);

/// <summary>The whole-turn rule the trimmer keeps: a turn is never split from its tool calls or results.</summary>
internal static class ConversationTurns {

    /// <summary>Splits a transcript into its system prompt and whole turns.</summary>
    /// <param name="messages">The transcript, in provider order.</param>
    /// <returns>The system prompt, when present, and the turns.</returns>
    /// <remarks>
    /// Only a leading <c>system</c> message is the system prompt; a later one is ordinary turn content.
    /// A turn begins at each <c>user</c> message, or at the first message when it is not a user message,
    /// which is the rule <see cref="ContextTrimmer"/> has always applied.
    /// </remarks>
    public static ConversationSplit Split(IReadOnlyList<ChatMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        int start = 0;
        ChatMessage? system = null;
        if (messages.Count > 0 && messages[0].Role == "system") {
            system = messages[0];
            start = 1;
        }

        List<IReadOnlyList<ChatMessage>> turns = [];
        List<ChatMessage>? current = null;
        for (int index = start; index < messages.Count; index++) {
            ChatMessage message = messages[index];
            if (current is null || message.Role == "user") {
                current = [];
                turns.Add(current);
            }
            current.Add(message);
        }

        return new ConversationSplit(system, turns);
    }

    /// <summary>Rebuilds a transcript from the system prompt and the turns that were kept.</summary>
    /// <param name="system">The system prompt, when the transcript had one.</param>
    /// <param name="turns">The turns to keep, oldest first.</param>
    /// <returns>The messages, in provider order.</returns>
    public static List<ChatMessage> Flatten(ChatMessage? system, IEnumerable<IReadOnlyList<ChatMessage>> turns) {
        ArgumentNullException.ThrowIfNull(turns);

        List<ChatMessage> messages = system is null ? [] : [system];
        foreach (IReadOnlyList<ChatMessage> turn in turns) {
            messages.AddRange(turn);
        }
        return messages;
    }
}
