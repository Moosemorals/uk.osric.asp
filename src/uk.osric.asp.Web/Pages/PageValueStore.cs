// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;

/// <summary>One subject's <see cref="IValueStore"/>, bound to the layer's page.</summary>
/// <remarks>
/// <para>
/// The Page subject's handle writes <c>page:</c> and may carry <see cref="StoreLifetimeKind.Page"/> or
/// <see cref="StoreLifetimeKind.NextRead"/>. The Visitor subject's writes <c>session:</c>,
/// <c>browser:</c> or <c>account:</c> depending on the lifetime, reads all three in order, and slides
/// the session's idle clock exactly as the old session store did.
/// </para>
/// <para>
/// A view with <see cref="_only"/> set is the obsolete adapters' shim: it reads and writes one
/// lifetime and nothing else, which is what keeps <see cref="IPageSession"/> behaving as it did for a
/// page that has not been edited.
/// </para>
/// </remarks>
internal sealed class PageValueStore : RenderBoundCapability, IValueStore {

    // One instance is enough: JsonSerializerOptions is thread-safe once used, and every store
    // serialises the same way the old page and session stores did.
    private static readonly JsonSerializerOptions s_json = new(JsonSerialization.Options) {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PagePath _bound;
    private readonly IStoreValues _store;
    private readonly IPageSessionStore? _sessions;
    private readonly PageReaderSubject? _reader;
    private readonly PageStoreCaps _pageCaps;
    private readonly int _maxKeyChars;
    private readonly bool _writable;
    private readonly PageCommitState? _commit;
    private readonly StoreLifetimeKind? _only;
    private readonly bool _slides = true;
    private readonly Func<CancellationToken, Task>? _validate;

    /// <summary>Creates a Page subject's handle.</summary>
    /// <param name="bound">The layer's own key.</param>
    /// <param name="store">Where values live.</param>
    /// <param name="caps">The Page subject's caps.</param>
    /// <param name="maxKeyChars">The longest key.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="validate">Checks a <c>For</c> key on first use, or <see langword="null"/>.</param>
    public PageValueStore(
        PagePath bound,
        IStoreValues store,
        PageStoreCaps caps,
        int maxKeyChars,
        bool writable,
        Func<CancellationToken, Task>? validate = null) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxKeyChars, 1);

        _bound = bound;
        _store = store;
        _pageCaps = caps;
        _maxKeyChars = maxKeyChars;
        _writable = writable;
        _validate = validate;
    }

    /// <summary>Creates a Visitor subject's handle.</summary>
    /// <param name="bound">The layer's own key.</param>
    /// <param name="store">Where values live.</param>
    /// <param name="sessions">Where the session row is minted.</param>
    /// <param name="reader">The reader's subjects.</param>
    /// <param name="maxKeyChars">The longest key.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="commit">Whether the response was flushed, or <see langword="null"/>.</param>
    /// <param name="only">The one lifetime this handle may see, or <see langword="null"/> for all.</param>
    /// <param name="slides">Whether a read slides the session's idle clock.</param>
    public PageValueStore(
        PagePath bound,
        IStoreValues store,
        IPageSessionStore sessions,
        PageReaderSubject reader,
        int maxKeyChars,
        bool writable,
        PageCommitState? commit,
        StoreLifetimeKind? only = null,
        bool slides = true) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxKeyChars, 1);

        _bound = bound;
        _store = store;
        _sessions = sessions;
        _reader = reader;
        _pageCaps = new PageStoreCaps(0, 0, 0);
        _maxKeyChars = maxKeyChars;
        _writable = writable;
        _commit = commit;
        _only = only;
        _slides = slides;
    }

    /// <summary>A view of this Visitor handle that sees one lifetime only, for the old adapters.</summary>
    /// <param name="kind">The lifetime to keep.</param>
    /// <param name="slides">Whether a read slides the session's idle clock.</param>
    /// <param name="writable">Whether the view may write, or <see langword="null"/> for the parent's rule.</param>
    /// <returns>The restricted handle.</returns>
    internal PageValueStore RestrictedTo(StoreLifetimeKind kind, bool slides = true, bool? writable = null) {
        ArgumentNullException.ThrowIfNull(_reader);
        ArgumentNullException.ThrowIfNull(_sessions);

        PageValueStore view = new(
            _bound, _store, _sessions, _reader, _maxKeyChars, writable ?? _writable, _commit, kind, slides);
        view.AttachDeadline(Deadline);

        return view;
    }

    /// <inheritdoc/>
    public async Task<PageValue<T>> TryGetValueAsync<T>(string key) =>
        await ReadAsync<T>(_bound.Key, key, CancellationToken.None);

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string pageKey, string key) {
        PagePath other = PagePath.Normalize(pageKey);
        if (!other.IsAddressable) {
            throw new ArgumentException($"{other} cannot name a page.", nameof(pageKey));
        }

        return ReadAsync<T>(other.Key, key, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<PageValue<T>> TryTakeAsync<T>(string key) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        string? pageKey = _reader is null ? _bound.Key : null;
        StoreValueRow? row = await _store.TakeAsync(Subjects(), pageKey, key, Cancellation());
        if (row is null) {
            return new PageValue<T>(false, default);
        }

        return To<T>(row);
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, StoreLifetime? lifetime = null) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        StoreLifetimeKind kind = Resolve(lifetime ?? StoreLifetime.Default);
        if (_only is not null && kind != _only) {
            throw new ArgumentException($"{_bound.Key} holds only {_only} values in this store.", nameof(lifetime));
        }

        PageStoreCaps caps = CapsFor(kind);
        string json = JsonSerializer.Serialize(value, s_json);
        if (Encoding.UTF8.GetByteCount(json) > caps.MaxValueBytes) {
            throw new ArgumentException(
                $"The value for {key} is larger than the {caps.MaxValueBytes} byte limit.", nameof(value));
        }

        if (_reader is not null && kind == StoreLifetimeKind.Browser && reader().BrowserId is null) {
            ThrowIfBrowserCannotBeSet();
        }

        if (_reader is not null && kind != StoreLifetimeKind.Browser) {
            ThrowIfSessionCannotBeSet();
        }

        string subject = SubjectFor(kind);

        if (_reader is not null && kind != StoreLifetimeKind.Browser) {
            await EnsureAsync(Cancellation());
        }

        string? expiresAt = lifetime?.ExpiresAt is { } expiry ? StoreSubjects.Stamp(expiry) : null;
        await _store.WriteAsync(
            new StoreValueWrite(subject, _bound.Key, key, (int)kind, expiresAt, json, RequireLivePage: _reader is null),
            caps,
            Cancellation());
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        return await _store.DeleteAsync(Subjects(), _bound.Key, key, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoreEntry>> KeysAsync(StoreFilter? filter = null) =>
        await ListAsync(_bound.Key, filter, CancellationToken.None);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoreEntry>> AllKeysAsync(StoreFilter? filter = null) =>
        await ListAsync(_reader is null ? _bound.Key : null, filter, CancellationToken.None);

    private async Task<PageValue<T>> ReadAsync<T>(string pageKey, string key, CancellationToken cancellationToken) {
        ValidateKey(key);
        ThrowIfEnded();
        await TouchAsync(cancellationToken);

        StoreLifetimeKind? lifetime = _only;
        if (lifetime is null && pageKey == _bound.Key && _reader is null) {
            lifetime = null;
        }

        IReadOnlyList<string> subjects = _reader is null
            ? [StoreSubjects.PagePrefix + pageKey]
            : _reader.ReadSubjects;
        StoreValueRow? row = await _store.FindAsync(subjects, pageKey, key, lifetime, Cancellation());
        return row is null ? new PageValue<T>(false, default) : To<T>(row);
    }

    private async Task<IReadOnlyList<StoreEntry>> ListAsync(
        string? pageKey,
        StoreFilter? filter,
        CancellationToken cancellationToken) {
        ThrowIfEnded();
        await TouchAsync(cancellationToken);

        StoreLifetimeKind? lifetime = _only;
        if (lifetime is null && filter?.Lifetime is { } chosen) {
            lifetime = chosen;
        } else if (lifetime is not null && filter?.Lifetime is { } other && other != lifetime) {
            return [];
        }

        int cap = _reader is null ? _pageCaps.MaxKeys : _reader.CapsFor(_only ?? StoreLifetimeKind.Session).MaxKeys;
        int limit = Math.Clamp(filter?.Limit ?? cap, 1, cap);

        IReadOnlyList<StoreValueRow> rows = await _store.ListAsync(
            Subjects(), pageKey, lifetime, limit, Cancellation());

        return [.. rows.Select(row => new StoreEntry(
            row.PageKey,
            row.Key,
            StoreLifetime.From((StoreLifetimeKind)row.Lifetime, Parse(row.ExpiresAt))))];
    }

    private StoreLifetimeKind Resolve(StoreLifetime lifetime) {
        bool isPage = _reader is null;
        StoreLifetimeKind kind = lifetime.Kind == StoreLifetimeKind.Default
            ? _only ?? (isPage ? StoreLifetimeKind.Page : StoreLifetimeKind.Person)
            : lifetime.Kind;

        if (isPage && kind is StoreLifetimeKind.Person or StoreLifetimeKind.Session or StoreLifetimeKind.Browser) {
            throw new ArgumentException(
                $"A Page subject cannot carry a {kind} lifetime; use Page or NextRead.", nameof(lifetime));
        }

        if (!isPage && kind == StoreLifetimeKind.Page) {
            throw new ArgumentException(
                "A Visitor subject cannot carry a Page lifetime; use Person, Session, Browser or NextRead.",
                nameof(lifetime));
        }

        return kind;
    }

    private PageStoreCaps CapsFor(StoreLifetimeKind kind) =>
        _reader is null ? _pageCaps : _reader.CapsFor(kind);

    private string SubjectFor(StoreLifetimeKind kind) {
        if (_reader is null) {
            return StoreSubjects.ForPage(_bound);
        }

        return _reader.SubjectFor(kind);
    }

    private IReadOnlyList<string> Subjects() =>
        _reader is null ? [StoreSubjects.ForPage(_bound)] : _reader.ReadSubjects;

    private PageReaderSubject reader() => _reader ?? throw new InvalidOperationException("A Page subject has no reader.");

    private static PageValue<T> To<T>(StoreValueRow row) => new(true, JsonSerializer.Deserialize<T>(row.Value, s_json)) {
        Lifetime = StoreLifetime.From((StoreLifetimeKind)row.Lifetime, Parse(row.ExpiresAt)),
        Page = row.PageKey,
    };

    private static DateTimeOffset? Parse(string? stamp) =>
        stamp is null ? null : DateTimeOffset.Parse(stamp, System.Globalization.CultureInfo.InvariantCulture);

    private Task EnsureAsync(CancellationToken cancellationToken) =>
        _reader is null ? Task.CompletedTask : _reader.EnsureSessionAsync(_sessions!, cancellationToken);

    private Task TouchAsync(CancellationToken cancellationToken) =>
        _reader is null || !_slides ? Task.CompletedTask : _reader.TouchSessionAsync(_sessions!, cancellationToken);

    // The old session store's rule: a first write after the response was flushed cannot mint a
    // session the browser will never learn about.
    private void ThrowIfSessionCannotBeSet() {
        if (_commit?.Committed == true && _reader is not null && !_reader.SessionExists) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a session after its response was flushed; write the session before the first flush.");
        }
    }

    private void ThrowIfBrowserCannotBeSet() {
        if (_commit?.Committed == true) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a browser identity after its response was flushed; write the preference before the first flush.");
        }
    }

    private void ValidateKey(string key) => PageStateKey.Validate(key, _maxKeyChars);

    private void ThrowIfReadOnly() {
        if (!_writable) {
            throw new InvalidOperationException($"{_bound.Key} may not write state during a HEAD request.");
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
