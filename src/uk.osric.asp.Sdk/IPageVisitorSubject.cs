// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>This person's subject: the shapes a visitor has that a page does not.</summary>
/// <remarks>
/// The type exists so a shape a visitor has — the message queue — can be offered here without ever
/// being offered to a page, whose subject is shared by everyone.
/// </remarks>
public interface IPageVisitorSubject : IPageSubject {

    /// <summary>This person's messages: durable, addressed to a page or a topic, and taken once.</summary>
    public IMessageStore Messages { get; }
}
