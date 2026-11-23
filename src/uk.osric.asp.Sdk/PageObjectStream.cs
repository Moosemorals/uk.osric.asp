// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The bytes a reference names, read in chunks as the render goes.</summary>
/// <remarks>
/// <para>
/// A reader is a <see cref="Stream"/> because that is the familiar shape for bytes, but the members a
/// page calls are declared here: only asynchronous reads exist, so the render's deadline can cancel a
/// slow one, and synchronous <c>Read</c>, <c>Seek</c> and <c>Write</c> throw. Dispose it with
/// <c>await using</c>; the render's cleanup closes one the page forgot.
/// </para>
/// <para>
/// The reader holds the connection the bytes come from until it is disposed, so read what you need
/// and close it rather than leaving it to the end of the render. <see cref="Length"/> and
/// <see cref="ContentType"/> are answered without reading the bytes, and the bytes are bounded by the
/// store's object limit.
/// </para>
/// </remarks>
public abstract class PageObjectStream : Stream {

    private long _position;
    private bool _disposed;

    /// <summary>The lowercase hexadecimal SHA-256 digest the bytes are addressed by.</summary>
    public abstract string Sha { get; }

    /// <summary>The canonical content type from the store's allow-list.</summary>
    public abstract string ContentType { get; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <summary>How many bytes the object holds.</summary>
    public abstract override long Length { get; }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">A reader is not seekable.</exception>
    public override long Position {
        get => _position;
        set => throw new NotSupportedException("An object reader is not seekable.");
    }

    /// <summary>Refused, because a blocking read cannot be cancelled by the render's deadline.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Use ReadAsync; a page's reads are asynchronous so a deadline can cancel one.");

    /// <summary>Refused, because a blocking read cannot be cancelled by the render's deadline.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int Read(Span<byte> buffer) =>
        throw new NotSupportedException("Use ReadAsync; a page's reads are asynchronous so a deadline can cancel one.");

    /// <summary>Refused, because a blocking read cannot be cancelled by the render's deadline.</summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int ReadByte() =>
        throw new NotSupportedException("Use ReadAsync; a page's reads are asynchronous so a deadline can cancel one.");

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length || count > buffer.Length - offset) {
            throw new ArgumentException("The offset and count do not fit the buffer.", nameof(count));
        }

        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty) {
            return 0;
        }

        long remaining = Length - _position;
        if (remaining <= 0) {
            return 0;
        }

        int wanted = (int)Math.Min(buffer.Length, remaining);
        int read = await ReadCoreAsync(buffer[..wanted], _position, cancellationToken);
        _position += read;

        return read;
    }

    /// <summary>Refused, because a reader is not seekable.</summary>
    /// <param name="offset">Ignored.</param>
    /// <param name="origin">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("An object reader is not seekable.");

    /// <summary>Refused, because a stored object is immutable.</summary>
    /// <param name="value">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value) => throw new NotSupportedException("A stored object is immutable.");

    /// <summary>Refused, because a stored object is immutable.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("A stored object is immutable.");

    /// <summary>Refused, because a stored object is immutable.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException("A stored object is immutable.");

    /// <summary>Refused, because a stored object is immutable.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A stored object is immutable.");

    /// <summary>Refused, because a stored object is immutable.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A stored object is immutable.");

    /// <summary>A read-only reader has nothing to flush.</summary>
    public override void Flush() {
    }

    /// <summary>A read-only reader has nothing to flush.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        await DisposeCoreAsync();
        GC.SuppressFinalize(this);
        await base.DisposeAsync();
    }

    /// <summary>Reads the next chunk from the store.</summary>
    /// <param name="buffer">The destination, already trimmed to what is left of the object.</param>
    /// <param name="offset">How far into the object this chunk starts.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many bytes were read, or zero at the end.</returns>
    protected abstract ValueTask<int> ReadCoreAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken);

    /// <summary>Releases the connection the bytes come from.</summary>
    protected abstract void DisposeCore();

    /// <summary>Releases the connection the bytes come from.</summary>
    /// <returns>A task that completes when it is released.</returns>
    protected abstract ValueTask DisposeCoreAsync();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                DisposeCore();
            }
        }

        base.Dispose(disposing);
    }
}
