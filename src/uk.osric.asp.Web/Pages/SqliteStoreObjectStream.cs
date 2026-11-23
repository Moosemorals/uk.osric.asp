// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using SQLitePCL;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>One stored object, read from its BLOB through SQLite's incremental I/O.</summary>
/// <remarks>
/// The reader owns the context and the connection the blob was opened on; disposing it closes the blob
/// and gives both back. It is created by <see cref="SqliteStoreObjects"/> and never by a page.
/// </remarks>
internal sealed class SqliteStoreObjectStream : PageObjectStream, IServedObject {

    private readonly StorageDbContext _database;
    private readonly SqliteConnection _connection;
    private sqlite3_blob? _blob;
    private bool _closed;

    /// <summary>Takes ownership of an open blob and the context it lives on.</summary>
    /// <param name="database">The context whose connection is open.</param>
    /// <param name="connection">The connection the blob was opened on.</param>
    /// <param name="blob">The open blob handle.</param>
    /// <param name="subject">The owner's subject string.</param>
    /// <param name="sha">The digest the bytes are addressed by.</param>
    /// <param name="contentType">The canonical content type.</param>
    /// <param name="length">The blob's length in bytes.</param>
    public SqliteStoreObjectStream(
        StorageDbContext database,
        SqliteConnection connection,
        sqlite3_blob blob,
        string subject,
        string sha,
        string contentType,
        long length) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(blob);

        _database = database;
        _connection = connection;
        _blob = blob;
        Subject = subject;
        Sha = sha;
        ContentType = contentType;
        Length = length;
    }

    /// <summary>The owner's subject string, which decides whether <c>/o</c> may cache it publicly.</summary>
    public string Subject { get; }

    /// <summary>Whether a page subject owns the bytes, so the route may cache them publicly.</summary>
    public bool Public => Subject.StartsWith(StoreSubjectKeys.PagePrefix, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string Sha { get; }

    /// <inheritdoc/>
    public override string ContentType { get; }

    /// <inheritdoc/>
    public override long Length { get; }

    /// <inheritdoc/>
    protected override ValueTask<int> ReadCoreAsync(
        Memory<byte> buffer,
        long offset,
        CancellationToken cancellationToken) =>
        ReadAtAsync(buffer, offset, cancellationToken);

    /// <summary>Reads a slice at an absolute offset, which is how the route answers a <c>Range</c>.</summary>
    /// <param name="buffer">The destination.</param>
    /// <param name="offset">How far into the object the slice starts.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many bytes were read.</returns>
    public ValueTask<int> ReadAtAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (_blob is not { } blob) {
            return ValueTask.FromResult(0);
        }

        // A blob's length is SQLite's int, and the object limit keeps it there.
        int read = raw.sqlite3_blob_read(blob, buffer.Span, checked((int)offset));
        if (read != 0) {
            throw new InvalidOperationException(
                $"Reading object {Sha} failed with SQLite code {read}.");
        }

        return ValueTask.FromResult(buffer.Length);
    }

    /// <inheritdoc/>
    protected override void DisposeCore() {
        if (_closed) {
            return;
        }

        _closed = true;
        CloseBlob();
        _connection.Close();
        _database.Dispose();
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeCoreAsync() {
        if (_closed) {
            return;
        }

        _closed = true;
        CloseBlob();
        await _connection.CloseAsync();
        await _database.DisposeAsync();
    }

    private void CloseBlob() {
        if (_blob is { } blob) {
            _blob = null;
            _ = raw.sqlite3_blob_close(blob);
        }
    }
}
