// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using HostPageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>One layer's <see cref="IPageStores"/>: the Page and Visitor subjects, and the escape.</summary>
/// <remarks>
/// The subject is the binding: <see cref="Page"/> is bound to the layer's own key and
/// <see cref="Visitor"/> to the reader, and neither has an argument that names another. A layer's
/// role decides the response it owns, not what it reaches, so every layer gets the same handle and
/// the verbs refuse a <c>HEAD</c>, an abandoned render or a passed deadline.
/// </remarks>
internal sealed class PageStoresScope : RenderBoundCapability, IPageStores, IAsyncDisposable {

    private readonly PagePath _key;
    private readonly IRecordCatalog? _records;
    private readonly IStoreValues _store;
    private readonly PageStoresOptions _options;
    private readonly IPageReadStore _readStore;
    private readonly bool _writable;
    private readonly SubjectHandle _page;
    private readonly VisitorHandle _visitor;
    private readonly List<SqliteRecordStore> _stores = [];
    private CancellationToken _deadline;

    /// <summary>Creates the layer's handles.</summary>
    /// <param name="key">The layer's canonical key.</param>
    /// <param name="reader">The reader's subjects, resolved once by the endpoint.</param>
    /// <param name="store">Where values live.</param>
    /// <param name="messages">Where messages live.</param>
    /// <param name="reserved">The keys the site's own routes answer for, which a post cannot address.</param>
    /// <param name="sessions">Where the session row is minted.</param>
    /// <param name="options">The store's bounds.</param>
    /// <param name="readStore">Where a <c>For</c> key is judged a library or a worker.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="commit">Whether the response was flushed, or <see langword="null"/>.</param>
    /// <param name="records">Where a record handle's catalogue and database are read, or <see langword="null"/>.</param>
    /// <param name="activity">What a posted or taken message re-arms, or <see langword="null"/>.</param>
    public PageStoresScope(
        PagePath key,
        PageReaderSubject reader,
        IStoreValues store,
        IStoreMessages messages,
        ReservedPaths reserved,
        IPageSessionStore sessions,
        PageStoresOptions options,
        IPageReadStore readStore,
        bool writable,
        PageCommitState? commit,
        IRecordCatalog? records = null,
        Action? activity = null) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(readStore);

        _key = key;
        _records = records;
        _store = store;
        _options = options;
        _readStore = readStore;
        _writable = writable;

        PageValues = new PageValueStore(key, store, options.Page, options.MaxKeyChars, writable);
        VisitorValues = new PageValueStore(key, store, sessions, reader, options.MaxKeyChars, writable, commit);
        Messages = new PageMessageStore(
            key, messages, sessions, reader, readStore, reserved, options.Messages, writable, commit, activity);

        _page = new SubjectHandle(PageValues, RecordsFor(key, SqliteRecordStore.SubjectKind.Page, reader, writable));
        _visitor = new VisitorHandle(
            VisitorValues,
            Messages,
            RecordsFor(key, SqliteRecordStore.SubjectKind.Visitor, reader, writable));
    }

    /// <summary>The Page subject's value store, which the old storage adapter wraps.</summary>
    internal PageValueStore PageValues { get; }

    /// <summary>The Visitor subject's value store, which the old session adapter restricts.</summary>
    internal PageValueStore VisitorValues { get; }

    /// <summary>The Visitor subject's messages.</summary>
    internal PageMessageStore Messages { get; }

    /// <inheritdoc/>
    public IPageSubject Page => _page;

    /// <inheritdoc/>
    public IPageVisitorSubject Visitor => _visitor;

    /// <inheritdoc/>
    public IPageSubject For(string key) {
        PagePath other = PagePath.Normalize(key);

        if (!other.IsAddressable) {
            throw new ArgumentException($"{other} cannot name a library or a worker.", nameof(key));
        }

        Task? checkedOnce = null;
        Task ConfirmAsync(CancellationToken cancellationToken) => checkedOnce ??= ValidateAsync(other, cancellationToken);

        PageValueStore values = new(
            other,
            _store,
            _options.Page,
            _options.MaxKeyChars,
            _writable,
            ConfirmAsync);
        values.AttachDeadline(_deadline);
        if (IsAbandoned) {
            values.Abandon();
        }

        return new SubjectHandle(
            values,
            RecordsFor(other, SqliteRecordStore.SubjectKind.Page, null, _writable, ConfirmAsync));
    }

    // A declared table's lifetime is Core's, and the reader's subjects are the contract's: one enum
    // for the schema and one for the handle, mapped where a handle is made.
    private static StoreLifetimeKind KindOf(StoreTableLifetime lifetime) => lifetime switch {
        StoreTableLifetime.Person => StoreLifetimeKind.Person,
        StoreTableLifetime.Session => StoreLifetimeKind.Session,
        StoreTableLifetime.Browser => StoreLifetimeKind.Browser,
        _ => throw new ArgumentOutOfRangeException(
            nameof(lifetime), lifetime, "A visitor's table follows the person, the visit or the browser."),
    };

    // A record store owns its own unpooled connection, its temp views and its authorizer, so it is
    // made per handle and disposed with the layer rather than shared with anything else.
    private SqliteRecordStore? RecordsFor(
        PagePath source,
        SqliteRecordStore.SubjectKind kind,
        PageReaderSubject? reader,
        bool writable,
        Func<CancellationToken, Task>? validate = null) {
        if (_records is null) {
            return null;
        }

        SqliteRecordStore store = new(
            source,
            reader is null ? null : lifetime => reader.SubjectFor(KindOf(lifetime)),
            kind,
            _records,
            _options.Records,
            writable,
            validate);
        _stores.Add(store);
        return store;
    }

    /// <summary>Ends every record store this scope made: the render's cleanup, not the page's.</summary>
    public async ValueTask DisposeAsync() {
        foreach (SqliteRecordStore store in _stores) {
            await store.DisposeAsync();
        }

        _stores.Clear();
    }

    /// <inheritdoc/>
    public override void AttachDeadline(CancellationToken deadline) {
        base.AttachDeadline(deadline);
        _deadline = deadline;
        PageValues.AttachDeadline(deadline);
        VisitorValues.AttachDeadline(deadline);
        Messages.AttachDeadline(deadline);
    }

    /// <inheritdoc/>
    public override void Abandon() {
        base.Abandon();
        PageValues.Abandon();
        VisitorValues.Abandon();
        Messages.Abandon();
    }

    // The handle is bound before the head is read, because For is a synchronous call; the first use
    // of either shape proves the key is a live library or worker.
    private async Task ValidateAsync(PagePath key, CancellationToken cancellationToken) {
        PageReadHead? head = await _readStore.GetHeadAsync(key.Key, cancellationToken);

        if (head is null || head.IsDeleted || head.TemplateKind is not (HostPageKind.Library or HostPageKind.Worker)) {
            throw new ArgumentException(
                $"{key.Key} is not a live library or worker, so it has no store of its own.",
                nameof(key));
        }
    }

    private sealed class SubjectHandle(IValueStore values, IRecordStore? records) : IPageSubject {
        public IValueStore Values { get; } = values;

        public IRecordStore Records => records
            ?? throw new InvalidOperationException("This layer has no record store.");
    }

    private sealed class VisitorHandle(IValueStore values, IMessageStore messages, IRecordStore? records)
        : IPageVisitorSubject {
        public IValueStore Values { get; } = values;

        public IMessageStore Messages { get; } = messages;

        public IRecordStore Records => records
            ?? throw new InvalidOperationException("This layer has no record store.");
    }
}
