// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>This person's subject: the shapes a visitor has, as a page reaches them.</summary>
/// <remarks>
/// <para>
/// The subject is the binding: a handle is bound to one person, and a write uses it. A shape a
/// subject does not have is absent from the type, not a runtime surprise.
/// </para>
/// <para>
/// This type does not inherit from <see cref="IPageSubject"/>. A visitor's subject is not a kind of
/// page's: the two declare the shapes they offer, and a shape both have is declared by both. A
/// common base would claim substitutability, and would hand a visitor every shape a page gains,
/// including a shape only a page may have.
/// </para>
/// </remarks>
public interface IVisitorSubject {

    /// <summary>This person's key/value store.</summary>
    public IValueStore Values { get; }

    /// <summary>This person's declared tables.</summary>
    public IRecordStore Records { get; }

    /// <summary>This person's messages: durable, addressed to a page or a topic, and taken once.</summary>
    public IMessageStore Messages { get; }
}
