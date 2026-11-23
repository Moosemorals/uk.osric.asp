// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One subject's <see cref="IObjectStore"/>, bound to the layer's page.</summary>
/// <remarks>
/// <para>
/// The Page subject writes <c>page:</c> and its objects are public; the Visitor subject writes the
/// reader's subject and only that reader may open them. A put is a write and is refused a
/// <c>HEAD</c>, an abandoned render or a passed deadline; an open is a read that slides the visitor's
/// idle clock, and the reader it answers holds a connection until it is disposed.
/// </para>
/// <para>
/// Every reader this handle issued is closed when the layer ends, so a page that forgets to dispose
/// one leaks nothing beyond its render.
/// </para>
/// </remarks>
internal sealed class PageObjectStore : RenderBoundCapability, IObjectStore, IAsyncDisposable {

    private readonly PagePath _bound;
    private readonly IStoreObjects _store;
    private readonly IPageSessionStore? _sessions;
    private readonly PageReaderSubject? _reader;
    private readonly PageObjectOptions _limits;
    private readonly bool _writable;
    private readonly PageCommitState? _commit;
    private readonly Func<CancellationToken, Task>? _validate;
    private readonly bool _slides = true;
    private readonly List<PageObjectStream> _readers = [];

    /// <summary>Creates a Page subject's handle.</summary>
    /// <param name="bound">The layer's own key.</param>
    /// <param name="store">Where objects live.</param>
    /// <param name="limits">The object shape's limits.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="validate">Checks a <c>For</c> key on first use, or <see langword="null"/>.</param>
    public PageObjectStore(
        PagePath bound,
        IStoreObjects store,
        PageObjectOptions limits,
        bool writable,
        Func<CancellationToken, Task>? validate = null) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);

        _bound = bound;
        _store = store;
        _limits = limits;
        _writable = writable;
        _validate = validate;
    }

    /// <summary>Creates a Visitor subject's handle.</summary>
    /// <param name="bound">The layer's own key.</param>
    /// <param name="store">Where objects live.</param>
    /// <param name="sessions">Where the session row is minted.</param>
    /// <param name="reader">The reader's subjects.</param>
    /// <param name="limits">The object shape's limits.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="commit">Whether the response was flushed, or <see langword="null"/>.</param>
    /// <param name="slides">Whether a read slides the session's idle clock.</param>
    public PageObjectStore(
        PagePath bound,
        IStoreObjects store,
        IPageSessionStore sessions,
        PageReaderSubject reader,
        PageObjectOptions limits,
        bool writable,
        PageCommitState? commit,
        bool slides = true) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);

        _bound = bound;
        _store = store;
        _sessions = sessions;
        _reader = reader;
        _limits = limits;
        _writable = writable;
        _commit = commit;
        _slides = slides;
    }

    /// <inheritdoc/>
    public async Task<PageObjectRef> PutAsync(byte[] content, string contentType, StoreLifetime? lifetime = null) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentType);

        string canonical = PageObjectTypes.Canonical(contentType);
        ValidateSize(content.LongLength);
        ThrowIfReadOnly();
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());

        StoreLifetimeKind kind = Resolve(lifetime ?? StoreLifetime.Default);
        string subject = SubjectFor(kind);

        if (_reader is not null) {
            if (kind == StoreLifetimeKind.Browser && _reader.BrowserId is null) {
                ThrowIfBrowserCannotBeSet();
            }

            if (kind != StoreLifetimeKind.Browser) {
                ThrowIfSessionCannotBeSet();
                await _reader.EnsureSessionAsync(_sessions!, Cancellation());
            }
        }

        string sha = PageObjectHash.Of(content);
        string? expiresAt = lifetime?.ExpiresAt is { } expiry ? StoreSubjectKeys.Stamp(expiry) : null;

        await _store.WriteAsync(
            new StoreObjectWrite(
                subject, sha, content.LongLength, canonical, (int)kind, expiresAt, content, RequireLivePage: _reader is null),
            _limits,
            Cancellation());

        return new PageObjectRef(sha, content.LongLength, canonical);
    }

    /// <inheritdoc/>
    public async Task<PageObjectStream?> OpenAsync(string sha) {
        ValidateSha(sha);
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());
        await TouchAsync(Cancellation());

        PageObjectStream? reader = _reader is null
            ? await _store.OpenPageAsync(sha, Cancellation())
            : await _store.OpenAsync(_reader.ReadSubjects, sha, Cancellation());

        if (reader is not null) {
            // A page may open several objects at once, so the list it is closed from is guarded.
            lock (_readers) {
                _readers.Add(reader);
            }
        }

        return reader;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string sha) {
        ValidateSha(sha);
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());
        await TouchAsync(Cancellation());

        PageObjectStream? reader = _reader is null
            ? await _store.OpenPageAsync(sha, Cancellation())
            : await _store.OpenAsync(_reader.ReadSubjects, sha, Cancellation());

        if (reader is null) {
            return false;
        }

        await reader.DisposeAsync();
        return true;
    }

    /// <summary>Closes every reader this handle issued.</summary>
    public async ValueTask DisposeAsync() {
        foreach (PageObjectStream reader in _readers) {
            await reader.DisposeAsync();
        }

        _readers.Clear();
    }

    private void ValidateSize(long bytes) {
        if (bytes > _limits.MaxObjectBytes) {
            throw new ArgumentException(
                $"The object is {bytes} bytes and Pages:Stores:Objects:MaxObjectBytes is {_limits.MaxObjectBytes}.");
        }
    }

    private static void ValidateSha(string sha) {
        ArgumentNullException.ThrowIfNull(sha);

        if (!PageObjectHash.IsCanonical(sha)) {
            throw new ArgumentException(
                "An object is addressed by 64 lowercase hexadecimal SHA-256 characters.", nameof(sha));
        }
    }

    private StoreLifetimeKind Resolve(StoreLifetime lifetime) {
        bool isPage = _reader is null;
        StoreLifetimeKind kind = lifetime.Kind == StoreLifetimeKind.Default
            ? (isPage ? StoreLifetimeKind.Page : StoreLifetimeKind.Person)
            : lifetime.Kind;

        if (isPage && kind != StoreLifetimeKind.Page) {
            throw new ArgumentException(
                $"A Page subject carries only a Page lifetime, not {kind}.", nameof(lifetime));
        }

        if (!isPage && kind is StoreLifetimeKind.Page or StoreLifetimeKind.NextRead) {
            throw new ArgumentException(
                $"A Visitor subject cannot carry a {kind} object; use Person, Session or Browser.",
                nameof(lifetime));
        }

        return kind;
    }

    private string SubjectFor(StoreLifetimeKind kind) =>
        _reader is null ? StoreSubjectKeys.ForPage(_bound) : _reader.SubjectFor(kind);

    // For's proof that the key is a live library or worker runs on first use, and the caller's
    // delegate memoises the read so every later call answers at once.
    private async Task ConfirmedAsync(CancellationToken cancellationToken) {
        if (_validate is not null) {
            await _validate(cancellationToken);
        }
    }

    private Task TouchAsync(CancellationToken cancellationToken) =>
        _reader is null || !_slides || !_writable
            ? Task.CompletedTask
            : _reader.TouchSessionAsync(_sessions!, cancellationToken);

    // A first write after the response was flushed cannot mint an identity the browser will never
    // learn about, so it is refused rather than stored where nobody can find it.
    private void ThrowIfSessionCannotBeSet() {
        if (_commit?.Committed == true && _reader is not null && !_reader.SessionExists) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a session after its response was flushed; store the object before the first flush.");
        }
    }

    private void ThrowIfBrowserCannotBeSet() {
        if (_commit?.Committed == true) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a browser identity after its response was flushed; store the object before the first flush.");
        }
    }

    private void ThrowIfReadOnly() {
        if (!_writable) {
            throw new InvalidOperationException($"{_bound.Key} may not store an object during a HEAD request.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {_bound.Key} was abandoned; it cannot use storage.");
        }

        Deadline.ThrowIfCancellationRequested();
    }

    private CancellationToken Cancellation() => Deadline;
}
