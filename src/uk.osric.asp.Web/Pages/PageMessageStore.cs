// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using HostPageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>One Visitor subject's <see cref="IMessageStore"/>, bound to the layer's page.</summary>
/// <remarks>
/// A post names its destination and is stamped with the layer's own key; a take and a peek read the
/// bound page's queue. The reader is always the owner, so the store filters by the reader's subjects
/// and the value a page sees carries no owner. A take writes and is refused a <c>HEAD</c> or an
/// abandoned render; a peek reads, so a <c>HEAD</c> and a layout may use it.
/// </remarks>
internal sealed class PageMessageStore : RenderBoundCapability, IMessageStore {

    private readonly PagePath _bound;
    private readonly IStoreMessages _store;
    private readonly IPageSessionStore _sessions;
    private readonly PageReaderSubject _reader;
    private readonly IPageReadStore _readStore;
    private readonly ReservedPaths _reserved;
    private readonly PageMessageOptions _limits;
    private readonly bool _writable;
    private readonly PageCommitState? _commit;
    private readonly Action? _activity;

    /// <summary>Creates the layer's message store.</summary>
    /// <param name="bound">The layer's own key, which a post is stamped with and a take reads.</param>
    /// <param name="store">Where messages live.</param>
    /// <param name="sessions">Where the session row is minted.</param>
    /// <param name="reader">The reader's subjects.</param>
    /// <param name="readStore">Where a destination's kind is judged.</param>
    /// <param name="reserved">The keys the site's own routes answer for.</param>
    /// <param name="limits">The message shape's limits.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="commit">Whether the response was flushed, or <see langword="null"/>.</param>
    /// <param name="activity">What a successful post or take re-arms, or <see langword="null"/>.</param>
    public PageMessageStore(
        PagePath bound,
        IStoreMessages store,
        IPageSessionStore sessions,
        PageReaderSubject reader,
        IPageReadStore readStore,
        ReservedPaths reserved,
        PageMessageOptions limits,
        bool writable,
        PageCommitState? commit,
        Action? activity) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(readStore);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(limits);

        _bound = bound;
        _store = store;
        _sessions = sessions;
        _reader = reader;
        _readStore = readStore;
        _reserved = reserved;
        _limits = limits;
        _writable = writable;
        _commit = commit;
        _activity = activity;
    }

    /// <inheritdoc/>
    public async Task<PageMessagePostResult> PostAsync(string toPageKey, string body, StoreLifetime? lifetime = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toPageKey);

        PagePath target = PagePath.Normalize(toPageKey);
        StoreLifetimeKind kind = Resolve(lifetime);
        ValidateBody(body);
        ThrowIfReadOnly();
        ThrowIfEnded();

        if (await AddressRefusalAsync(target, Cancellation()) is { } refusal) {
            return new PageMessagePostResult(refusal);
        }

        return await WriteAsync(target.Key, topic: null, body, kind, lifetime, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<PageMessagePostResult> PostAsync(PageTopic topic, string body, StoreLifetime? lifetime = null) {
        ArgumentNullException.ThrowIfNull(topic);

        StoreLifetimeKind kind = Resolve(lifetime);
        ValidateBody(body);
        ThrowIfReadOnly();
        ThrowIfEnded();

        return await WriteAsync(toPageKey: null, topic.Name, body, kind, lifetime, Cancellation());
    }

    /// <inheritdoc/>
    public Task<PageMessageRead> TakeAsync() {
        ThrowIfReadOnly();
        ThrowIfEnded();

        return TakeFromAsync(_bound.Key, topic: null, Cancellation());
    }

    /// <inheritdoc/>
    public Task<PageMessageRead> TakeAsync(PageTopic topic) {
        ArgumentNullException.ThrowIfNull(topic);
        ThrowIfReadOnly();
        ThrowIfEnded();

        return TakeFromAsync(toPageKey: null, topic.Name, Cancellation());
    }

    /// <inheritdoc/>
    public Task<PageMessageRead> PeekAsync() => PeekFromAsync(_bound.Key, topic: null, Cancellation());

    /// <inheritdoc/>
    public Task<PageMessageRead> PeekAsync(PageTopic topic) {
        ArgumentNullException.ThrowIfNull(topic);

        return PeekFromAsync(toPageKey: null, topic.Name, Cancellation());
    }

    private async Task<PageMessagePostResult> WriteAsync(
        string? toPageKey,
        string? topic,
        string body,
        StoreLifetimeKind kind,
        StoreLifetime? lifetime,
        CancellationToken cancellationToken) {
        ThrowIfIdentityCannotBeSet(kind);
        string subject = _reader.SubjectFor(kind);
        if (kind != StoreLifetimeKind.Browser) {
            await _reader.EnsureSessionAsync(_sessions, cancellationToken);
        }

        string? expiresAt = lifetime?.ExpiresAt is { } expiry ? StoreSubjectKeys.Stamp(expiry) : null;
        bool posted = await _store.PostAsync(
            new StoreMessageWrite(subject, toPageKey, topic, _bound.Key, body, (int)kind, expiresAt),
            _limits,
            cancellationToken);

        if (posted) {
            _activity?.Invoke();
        }

        return new PageMessagePostResult(posted ? PageMessageRefusal.None : PageMessageRefusal.Full);
    }

    private async Task<PageMessageRead> TakeFromAsync(string? toPageKey, string? topic, CancellationToken cancellationToken) {
        StoreMessageRow? row = await _store.TakeAsync(_reader.ReadSubjects, toPageKey, topic, cancellationToken);
        if (row is null) {
            return PageMessageRead.Empty;
        }

        _activity?.Invoke();

        return Read(row);
    }

    private async Task<PageMessageRead> PeekFromAsync(string? toPageKey, string? topic, CancellationToken cancellationToken) {
        ThrowIfEnded();

        // A read slides the visitor's idle clock, as a value read does; a HEAD is a read that may not
        // write at all, so it does not.
        if (_writable) {
            await _reader.TouchSessionAsync(_sessions, cancellationToken);
        }

        StoreMessageRow? row = await _store.PeekAsync(_reader.ReadSubjects, toPageKey, topic, cancellationToken);

        return row is null ? PageMessageRead.Empty : Read(row);
    }

    private async Task<PageMessageRefusal?> AddressRefusalAsync(PagePath target, CancellationToken cancellationToken) {
        if (!target.IsAddressable) {
            return PageMessageRefusal.NotAddressable;
        }

        if (_reserved.IsReserved(target)) {
            return PageMessageRefusal.OwnedBySite;
        }

        PageReadHead? head = await _readStore.GetHeadAsync(target.Key, cancellationToken);
        if (head is null || head.IsDeleted) {
            return PageMessageRefusal.UnknownPage;
        }

        // A library is declarations only and a text asset has no code, so neither can ever take one. A
        // source this build has not judged yet is let through: it is a page until it says otherwise,
        // and a destination that never renders can only cost the sender one undeliverable message.
        return head.TemplateKind is HostPageKind.Library or HostPageKind.Text ? PageMessageRefusal.NotASink : null;
    }

    // A message is durable, so a one-shot lifetime and a Page lifetime are both page bugs: the flash a
    // redirect reads is a value, and a message has no page subject to die with.
    private static StoreLifetimeKind Resolve(StoreLifetime? lifetime) {
        StoreLifetimeKind kind = lifetime?.Kind ?? StoreLifetimeKind.Default;

        return kind switch {
            StoreLifetimeKind.Default => StoreLifetimeKind.Person,
            StoreLifetimeKind.Person or StoreLifetimeKind.Session or StoreLifetimeKind.Browser => kind,
            _ => throw new ArgumentException(
                $"A message carries Person, Session or Browser, not {kind}; a one-shot is a value.", nameof(lifetime)),
        };
    }

    private void ValidateBody(string body) {
        ArgumentNullException.ThrowIfNull(body);

        int bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > _limits.MaxMessageBytes) {
            throw new ArgumentException(
                $"The message is {bytes} bytes and {_limits.MaxMessageBytes} is the limit.", nameof(body));
        }
    }

    // The old session store's rule: an identity minted after the response was flushed can never reach
    // the browser, so a post that needs one is refused rather than stored where nobody can find it.
    private void ThrowIfIdentityCannotBeSet(StoreLifetimeKind kind) {
        if (_commit?.Committed != true) {
            return;
        }

        if (kind == StoreLifetimeKind.Browser && _reader.BrowserId is null) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a browser identity after its response was flushed; post before the first flush.");
        }

        if (kind != StoreLifetimeKind.Browser && !_reader.SessionExists) {
            throw new InvalidOperationException(
                $"{_bound.Key} cannot start a session after its response was flushed; post before the first flush.");
        }
    }

    private static PageMessageRead Read(StoreMessageRow row) => new(
        PageMessageRefusal.None,
        new PageMessage(
            row.FromPageKey,
            row.Body,
            DateTimeOffset.Parse(row.SentAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));

    private void ThrowIfReadOnly() {
        if (!_writable) {
            throw new InvalidOperationException($"{_bound.Key} may not post or take a message during a HEAD request.");
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
