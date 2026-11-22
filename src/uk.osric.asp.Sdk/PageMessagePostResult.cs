// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The answer to a posted message.</summary>
/// <param name="Refusal">Why it was refused, or <see cref="PageMessageRefusal.None"/>.</param>
public readonly record struct PageMessagePostResult(PageMessageRefusal Refusal) {

    /// <summary>Whether the message was queued.</summary>
    public bool Posted => Refusal is PageMessageRefusal.None;
}
