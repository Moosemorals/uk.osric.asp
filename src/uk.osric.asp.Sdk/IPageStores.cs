// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A page's data, by whose it is.</summary>
/// <remarks>
/// <para>
/// Two subjects: <see cref="Page"/>, shared by every visitor and bound to the layer's own key, and
/// <see cref="Visitor"/>, this person. The two are separate types, so a shape one subject does not
/// have cannot be reached through it. A page never sees a subject key, a session id or an account
/// name; the host resolves them once per request.
/// </para>
/// <para>
/// <see cref="For(string)"/> is the one escape, for a library's or a worker's own namespace; it
/// answers a Page-shaped handle, because that key is a page's own. A page reaches a library's data
/// by calling the library's methods, never by naming its tables.
/// </para>
/// </remarks>
public interface IPageStores {

    /// <summary>The Page subject: this layer's page, shared by every visitor.</summary>
    public IPageSubject Page { get; }

    /// <summary>The Visitor subject: the person reading, bound to this layer's page.</summary>
    public IVisitorSubject Visitor { get; }

    /// <summary>A library's or a worker's own subject, with the caller's reader.</summary>
    /// <param name="key">The canonical key of a live library or worker.</param>
    /// <returns>A Page-shaped handle bound to that key.</returns>
    /// <exception cref="System.ArgumentException">The key names no live library or worker.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public IPageSubject For(string key);
}
