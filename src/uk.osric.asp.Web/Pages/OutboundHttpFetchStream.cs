// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using System.Text;
using uk.osric.asp.Sdk;

/// <summary>Reads one outbound response as bounded text lines.</summary>
internal sealed class OutboundHttpFetchStream : HttpFetchStream, IAsyncDisposable {

    private readonly CancellationToken _aborted;
    private readonly CancellationToken _deadline;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private readonly TimeSpan _silence;
    private readonly TimeSpan _maximum;
    private readonly Action<HttpFetchStream>? _onClosed;
    private readonly object _readSync = new();
    private readonly object _closeSync = new();
    private readonly Stopwatch _started = Stopwatch.StartNew();
    private Task? _closeTask;
    private int _failure;
    private int _truncated;
    private int _closed;
    private int _completed;

    public OutboundHttpFetchStream(
        HttpResponseMessage response,
        Stream body,
        IReadOnlyDictionary<string, string> headers,
        HttpFetchFailure failure,
        Encoding encoding,
        TimeSpan silence,
        TimeSpan maximum,
        int maxBytes,
        CancellationToken aborted,
        CancellationToken deadline,
        Action<HttpFetchStream>? onClosed = null) {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(encoding);

        _response = response;
        Headers = headers;
        _failure = (int)failure;
        _silence = silence;
        _maximum = maximum;
        _aborted = aborted;
        _deadline = deadline;
        _onClosed = onClosed;
        _reader = new StreamReader(
            new LimitedReadStream(body, maxBytes, MarkTruncated),
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
    }

    /// <inheritdoc/>
    public override int Status => (int)_response.StatusCode;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, string> Headers { get; }

    /// <inheritdoc/>
    public override HttpFetchFailure Failure => (HttpFetchFailure)Volatile.Read(ref _failure);

    /// <inheritdoc/>
    public override bool Truncated => Volatile.Read(ref _truncated) != 0;

    /// <inheritdoc/>
    public override async Task<string?> ReadLineAsync() {
        Task wait;
        lock (_readSync) {
            if (Volatile.Read(ref _closed) != 0) {
                return null;
            }

            wait = _readGate.WaitAsync();
        }

        await wait;
        string? line = null;
        bool closeAfterRead = false;

        try {
            if (Volatile.Read(ref _closed) != 0) {
                return null;
            }

            TimeSpan remaining = _maximum - _started.Elapsed;
            if (remaining <= TimeSpan.Zero) {
                SetReadFailure(HttpFetchFailure.Timeout);
                closeAfterRead = true;
            } else {
                TimeSpan timeout = remaining < _silence ? remaining : _silence;
                using CancellationTokenSource silence = new();
                silence.CancelAfter(timeout);
                using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(
                    _aborted,
                    _deadline,
                    _closing.Token,
                    silence.Token);

                try {
                    line = await _reader.ReadLineAsync(read.Token);
                    closeAfterRead = line is null;
                } catch (OperationCanceledException) {
                    if (Volatile.Read(ref _closed) == 0) {
                        SetReadFailure(_aborted.IsCancellationRequested
                            ? HttpFetchFailure.Aborted
                            : HttpFetchFailure.Timeout);
                        closeAfterRead = true;
                    }
                } catch (HttpRequestException) {
                    if (Volatile.Read(ref _closed) == 0) {
                        SetReadFailure(HttpFetchFailure.Network);
                        closeAfterRead = true;
                    }
                } catch (IOException) {
                    if (Volatile.Read(ref _closed) == 0) {
                        SetReadFailure(HttpFetchFailure.Network);
                        closeAfterRead = true;
                    }
                }
            }
        } finally {
            _ = _readGate.Release();
        }

        if (closeAfterRead) {
            await CloseAsync();
        }

        return line;
    }

    /// <inheritdoc/>
    public override Task CloseAsync() {
        lock (_closeSync) {
            return _closeTask ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync() {
        await Task.Yield();
        lock (_readSync) {
            if (Interlocked.Exchange(ref _closed, 1) != 0) {
                return;
            }
        }

        await _closing.CancelAsync();
        await _readGate.WaitAsync();
        try {
            try {
                _reader.Dispose();
                _response.Dispose();
            } finally {
                Complete();
            }
        } finally {
            _ = _readGate.Release();
            _readGate.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void MarkTruncated() => Volatile.Write(ref _truncated, 1);

    private void SetReadFailure(HttpFetchFailure failure) {
        Volatile.Write(ref _failure, (int)failure);
        MarkTruncated();
    }

    private void Complete() {
        if (Interlocked.Exchange(ref _completed, 1) == 0) {
            _closing.Dispose();
            _onClosed?.Invoke(this);
        }
    }

    private sealed class LimitedReadStream(Stream source, int maxBytes, Action onLimitReached) : Stream {
        private readonly byte[] _probe = new byte[1];
        private int _remaining = maxBytes;
        private bool _ended;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) {
            if (_ended || buffer.IsEmpty) {
                return 0;
            }

            if (_remaining == 0) {
                bool hasMore = source.ReadByte() >= 0;
                _ended = true;
                if (hasMore) {
                    onLimitReached();
                }

                return 0;
            }

            int count = source.Read(buffer[..Math.Min(buffer.Length, _remaining)]);
            AfterRead(count);

            return count;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) {
            if (_ended || buffer.IsEmpty) {
                return 0;
            }

            if (_remaining == 0) {
                int extra = await source.ReadAsync(_probe.AsMemory(), cancellationToken);
                _ended = true;
                if (extra > 0) {
                    onLimitReached();
                }

                return 0;
            }

            int count = await source.ReadAsync(buffer[..Math.Min(buffer.Length, _remaining)], cancellationToken);
            AfterRead(count);

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
        }

        private void AfterRead(int count) {
            if (count == 0) {
                _ended = true;
                return;
            }

            _remaining -= count;
        }
    }
}
