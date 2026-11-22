// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The answer to a take or a peek: the message, or why there was none.</summary>
/// <param name="Refusal">Why nothing was answered, or <see cref="PageMessageRefusal.None"/>.</param>
/// <param name="Message">The message when one was answered.</param>
/// <remarks>
/// One value serves both calls, because the only difference between them is whether the row is still
/// there afterwards, and the caller knows which one it called.
/// </remarks>
public readonly record struct PageMessageRead(PageMessageRefusal Refusal, PageMessage? Message) {

    /// <summary>Whether a message was answered.</summary>
    public bool Found => Refusal is PageMessageRefusal.None && Message is not null;

    /// <summary>The answer when the queue held nothing.</summary>
    public static PageMessageRead Empty { get; } = new(PageMessageRefusal.Empty, null);
}
