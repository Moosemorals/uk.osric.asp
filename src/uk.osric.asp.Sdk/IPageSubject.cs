// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One subject's shapes, as a page reaches them.</summary>
/// <remarks>
/// The subject is the binding: a handle is bound to one subject key, and a write uses it. A shape a
/// subject does not have is absent from the type, not a runtime surprise.
/// </remarks>
public interface IPageSubject {

    /// <summary>The subject's key/value store.</summary>
    public IValueStore Values { get; }
}
