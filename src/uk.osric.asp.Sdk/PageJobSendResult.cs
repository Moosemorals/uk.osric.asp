// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The answer to a message sent to a job.</summary>
/// <param name="Refusal">Why it was refused, or <see cref="PageJobRefusal.None"/>.</param>
public readonly record struct PageJobSendResult(PageJobRefusal Refusal) {

    /// <summary>Whether the message was queued for the worker.</summary>
    public bool Sent => Refusal is PageJobRefusal.None;
}
