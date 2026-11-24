// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One subject's shapes, as a page reaches them.</summary>
/// <remarks>
/// <para>
/// The subject is the binding: a handle is bound to one subject key, and a write uses it. A shape a
/// subject does not have is absent from the type, not a runtime surprise.
/// </para>
/// <para>
/// This type does not inherit from <see cref="IVisitorSubject"/>. A page's subject is not a kind of
/// visitor's: the two declare the shapes they offer, and a shape both have is declared by both. A
/// common base would claim substitutability, and would offer a page every shape a visitor gains.
/// </para>
/// </remarks>
public interface IPageSubject {

    /// <summary>The subject's key/value store.</summary>
    public IValueStore Values { get; }

    /// <summary>The subject's declared tables.</summary>
    public IRecordStore Records { get; }

    /// <summary>The subject's stored bytes.</summary>
    public IObjectStore Objects { get; }

    /// <summary>The page's secrets: opaque text that nothing else returns.</summary>
    public ISecretStore Secrets { get; }
}
