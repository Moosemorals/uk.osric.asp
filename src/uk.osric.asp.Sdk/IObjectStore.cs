// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A subject's stored bytes, addressed by their SHA-256 digest.</summary>
/// <remarks>
/// <para>
/// Content addressing is the whole identity: the same bytes put twice under one subject are one row,
/// and a put replaces the lifetime the previous row carried. The digest is lowercase hexadecimal and
/// is what <c>/o/{sha}</c> serves, so a page that has a reference can write an <c>img</c>, a
/// <c>download</c> or a link that points at it.
/// </para>
/// <para>
/// A Page subject's object is public and immutable until the page goes; a Visitor subject's belongs to
/// the reader and only the reader's subjects may open it. A content type outside the store's fixed
/// allow-list is refused before anything is written, and an object over its per-object or per-subject
/// limit throws when it is awaited.
/// </para>
/// <para>
/// A read is a <see cref="PageObjectStream"/>, so the bytes leave the store in chunks rather than all
/// at once. It holds the connection until it is disposed, so use <c>await using</c>.
/// </para>
/// </remarks>
public interface IObjectStore {

    /// <summary>Stores bytes under the bound subject, replacing the lifetime the same bytes carried.</summary>
    /// <param name="content">The bytes; never <see langword="null"/>.</param>
    /// <param name="contentType">One of the store's fixed allow-list, whitespace and case tolerated.</param>
    /// <param name="lifetime">
    /// The object's lifetime, or <see langword="null"/> for the subject's own: <see cref="StoreLifetime.Page"/>
    /// on a Page subject, <see cref="StoreLifetime.Person"/> on a Visitor. A lifetime the subject cannot
    /// carry throws, and <see cref="StoreLifetime.NextRead"/> is not an object lifetime.
    /// </param>
    /// <returns>The address, length and canonical content type the bytes were stored with.</returns>
    /// <exception cref="System.ArgumentException">The content type is refused, or the bytes exceed a limit.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<PageObjectRef> PutAsync(byte[] content, string contentType, StoreLifetime? lifetime = null);

    /// <summary>Opens a reader over the bytes a reference names, when the subject may read them.</summary>
    /// <param name="sha">The lowercase hexadecimal SHA-256 digest.</param>
    /// <returns>The reader, or <see langword="null"/> when it is absent, expired or another subject's.</returns>
    /// <exception cref="System.ArgumentException">The digest is not a lowercase hexadecimal SHA-256.</exception>
    public Task<PageObjectStream?> OpenAsync(string sha);

    /// <summary>Reports whether a reference names bytes the subject may read.</summary>
    /// <param name="sha">The lowercase hexadecimal SHA-256 digest.</param>
    /// <returns>Whether the object exists and the subject may read it.</returns>
    /// <exception cref="System.ArgumentException">The digest is not a lowercase hexadecimal SHA-256.</exception>
    public Task<bool> ExistsAsync(string sha);
}
