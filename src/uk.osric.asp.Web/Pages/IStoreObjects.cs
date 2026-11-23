// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>One object about to be written.</summary>
/// <param name="Subject">The owner's subject string.</param>
/// <param name="Sha">The lowercase hexadecimal SHA-256 digest.</param>
/// <param name="Length">How many bytes the object holds.</param>
/// <param name="ContentType">The canonical content type.</param>
/// <param name="Lifetime">The resolved kind; never <c>Default</c> or <c>NextRead</c>.</param>
/// <param name="ExpiresAt">The fixed-width expiry, or <see langword="null"/>.</param>
/// <param name="Content">The bytes.</param>
/// <param name="RequireLivePage">Whether the page's head has to be live, which a Page write requires.</param>
internal sealed record StoreObjectWrite(
    string Subject,
    string Sha,
    long Length,
    string ContentType,
    int Lifetime,
    string? ExpiresAt,
    byte[] Content,
    bool RequireLivePage);

/// <summary>The bytes of one object the route may serve, with its cache policy.</summary>
/// <remarks>
/// The route needs a random-access read so a <c>Range</c> starts where it says, which the page's
/// forward-only reader deliberately does not offer. The concrete store hands out the same open
/// connection through both shapes.
/// </remarks>
internal interface IServedObject : IAsyncDisposable {

    /// <summary>The lowercase hexadecimal SHA-256 digest the bytes are addressed by.</summary>
    string Sha { get; }

    /// <summary>The canonical content type.</summary>
    string ContentType { get; }

    /// <summary>How many bytes the object holds.</summary>
    long Length { get; }

    /// <summary>Whether a page subject owns it, so the route may cache it publicly.</summary>
    bool Public { get; }

    /// <summary>Reads a slice at an absolute offset.</summary>
    /// <param name="buffer">The destination.</param>
    /// <param name="offset">How far into the object the slice starts.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many bytes were read.</returns>
    ValueTask<int> ReadAtAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken);
}

/// <summary>The object operations the shell uses, over <c>StoreObjects</c>.</summary>
/// <remarks>
/// A read opens the row's BLOB through SQLite's incremental blob API, so the bytes never have to be
/// held as one array. The reader owns the connection until it is disposed, and every call that opens
/// one also owns the context it came from: a render that was abandoned can outlive the request scope.
/// </remarks>
internal interface IStoreObjects {

    /// <summary>Opens any page subject's object by digest, which is what makes one public.</summary>
    /// <param name="sha">The lowercase hexadecimal SHA-256 digest.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The reader, or <see langword="null"/> when no page holds it.</returns>
    Task<PageObjectStream?> OpenPageAsync(string sha, CancellationToken cancellationToken = default);

    /// <summary>Opens one of the reader's own objects by digest.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="sha">The lowercase hexadecimal SHA-256 digest.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The reader, or <see langword="null"/> when the reader does not hold it.</returns>
    Task<PageObjectStream?> OpenAsync(
        IReadOnlyList<string> subjects,
        string sha,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the object <c>/o/{sha}</c> serves: a page's first, then the reader's own.</summary>
    /// <param name="sha">The lowercase hexadecimal SHA-256 digest.</param>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The reader, or <see langword="null"/> when nothing may be served.</returns>
    Task<IServedObject?> OpenServedAsync(
        string sha,
        IReadOnlyList<string> subjects,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one object under its subject's limits, in one statement.</summary>
    /// <param name="write">The row to write.</param>
    /// <param name="limits">The limits the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A limit was hit, or the page is no longer live.</exception>
    Task WriteAsync(StoreObjectWrite write, PageObjectOptions limits, CancellationToken cancellationToken = default);

    /// <summary>Removes expired objects and browser objects whose subject has gone quiet.</summary>
    /// <param name="browserLifetime">How long a browser subject survives after its last write.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many rows were removed.</returns>
    Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default);
}
