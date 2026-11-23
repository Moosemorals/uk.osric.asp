// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;

/// <summary>The reader's subjects, resolved once per request beside the session.</summary>
/// <remarks>
/// The reader is an account when signed in, a session while anonymous, and a browser whenever the
/// <c>b</c> cookie was presented. A read searches the session, then the browser, then the account, so
/// the more ephemeral value shadows the longer-lived one. The browser id is minted lazily, on the
/// first <c>Browser</c> write, and the response carries it afterwards.
/// </remarks>
internal sealed class PageReaderSubject {

    private readonly PageSessionRequest? _session;
    private readonly VisitorOptions _visitor;
    private readonly string? _jobAccount;
    private string? _browserId;

    /// <summary>Creates the reader's subjects.</summary>
    /// <param name="session">The request's session, resolved by the endpoint.</param>
    /// <param name="visitor">The visitor store's bounds.</param>
    /// <param name="browserId">The browser id the visitor presented, or <see langword="null"/>.</param>
    public PageReaderSubject(PageSessionRequest session, VisitorOptions visitor, string? browserId) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(visitor);

        _session = session;
        _visitor = visitor;
        _browserId = browserId;
        BrowserPresented = browserId is not null;
    }

    // A job has no cookie and no browser, so its reader is the starter's account and nothing else.
    private PageReaderSubject(string account, VisitorOptions visitor) {
        _session = null;
        _visitor = visitor;
        _jobAccount = account;
        IsJob = true;
    }

    /// <summary>A job's reader: the starter's account, with no session and no browser.</summary>
    /// <param name="visitor">The visitor store's bounds.</param>
    /// <param name="account">The normalised account the job runs for.</param>
    public static PageReaderSubject ForJob(VisitorOptions visitor, string account) {
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        return new PageReaderSubject(account, visitor);
    }

    /// <summary>Whether this reader is a job's, which has no session or browser at all.</summary>
    public bool IsJob { get; }

    /// <summary>Whether the request presented a browser cookie.</summary>
    public bool BrowserPresented { get; }

    /// <summary>The browser id, or <see langword="null"/> until one is minted.</summary>
    public string? BrowserId => _browserId;

    /// <summary>Whether the response has to carry the browser cookie.</summary>
    public bool NeedsBrowserCookie { get; private set; }

    /// <summary>The signed-in account, or <see langword="null"/>.</summary>
    public string? Account => _session?.Account ?? _jobAccount;

    /// <summary>The session id, whether the row exists yet or not; empty for a job.</summary>
    public string SessionId => _session?.Id ?? string.Empty;

    /// <summary>Whether the visitor is signed in.</summary>
    public bool IsSignedIn => Account is not null;

    /// <summary>Whether the session row exists for this request, or was minted by it.</summary>
    public bool SessionExists => _session?.Exists ?? false;

    /// <summary>Whether the response has to carry the session cookie.</summary>
    public bool NeedsSessionCookie => _session?.NeedsCookie ?? false;

    /// <summary>The subjects a read searches, most ephemeral first.</summary>
    public IReadOnlyList<string> ReadSubjects {
        get {
            if (IsJob) {
                return [StoreSubjectKeys.ForAccount(_jobAccount!)];
            }

            List<string> subjects = [StoreSubjectKeys.ForSession(SessionId)];
            if (_browserId is not null) {
                subjects.Add(StoreSubjectKeys.ForBrowser(_browserId));
            }

            if (Account is not null) {
                subjects.Add(StoreSubjectKeys.ForAccount(Account));
            }

            return subjects;
        }
    }

    /// <summary>The limits a subject kind obeys for this reader.</summary>
    /// <param name="kind">The resolved lifetime kind.</param>
    /// <exception cref="ArgumentOutOfRangeException">A Page lifetime was asked of a Visitor subject.</exception>
    public SubjectLimits LimitsFor(StoreLifetimeKind kind) {
        if (IsJob) {
            return kind is StoreLifetimeKind.Person or StoreLifetimeKind.NextRead
                ? _visitor.SignedIn
                : throw new ArgumentException("A job carries only a Person lifetime.", nameof(kind));
        }

        return kind switch {
            StoreLifetimeKind.Browser => _visitor.Browser,
            StoreLifetimeKind.Session or StoreLifetimeKind.Person or StoreLifetimeKind.NextRead => SessionLimits,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A Visitor subject has no Page lifetime."),
        };
    }

    /// <summary>The subject string a kind is written to, minting the browser when one is needed.</summary>
    /// <param name="kind">The resolved lifetime kind.</param>
    /// <exception cref="ArgumentOutOfRangeException">A Page lifetime was asked of a Visitor subject.</exception>
    /// <exception cref="InvalidOperationException">A browser write happened after the response was flushed.</exception>
    public string SubjectFor(StoreLifetimeKind kind) => kind switch {
        StoreLifetimeKind.Session => IsJob
            ? throw new ArgumentException("A job has no session, so it cannot carry a Session lifetime.", nameof(kind))
            : StoreSubjectKeys.ForSession(SessionId),
        StoreLifetimeKind.Browser => IsJob
            ? throw new ArgumentException("A job has no browser, so it cannot carry a Browser lifetime.", nameof(kind))
            : StoreSubjectKeys.ForBrowser(EnsureBrowser()),
        StoreLifetimeKind.Person or StoreLifetimeKind.NextRead =>
            Account is not null ? StoreSubjectKeys.ForAccount(Account) : StoreSubjectKeys.ForSession(SessionId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A Visitor subject has no Page lifetime."),
    };

    /// <summary>Mints the row on the request's first write, if it has not been minted already.</summary>
    /// <param name="store">Where the row lives.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task EnsureSessionAsync(IPageSessionStore store, CancellationToken cancellationToken = default) =>
        IsJob ? Task.CompletedTask : _session!.EnsureAsync(store, cancellationToken);

    /// <summary>Slides the session's idle timer, at most once per request.</summary>
    /// <param name="store">Where the row lives.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task TouchSessionAsync(IPageSessionStore store, CancellationToken cancellationToken = default) =>
        IsJob ? Task.CompletedTask : _session!.TouchAsync(store, cancellationToken);

    private SubjectLimits SessionLimits => IsSignedIn ? _visitor.SignedIn : _visitor.Anonymous;

    private string EnsureBrowser() {
        if (_browserId is null) {
            _browserId = PageBrowserCookie.Mint();
            NeedsBrowserCookie = true;
        }

        return _browserId;
    }
}

/// <summary>Resolves the reader's subjects once per request, beside the session.</summary>
internal static class PageReaderSubjectResolver {

    private static readonly object s_itemKey = new();

    /// <summary>Finds or builds the request's reader subjects.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="session">The session the endpoint resolved.</param>
    /// <param name="visitor">The visitor store's bounds.</param>
    /// <param name="options">The browser cookie's name.</param>
    /// <returns>The reader, cached on the request so every layer sees one.</returns>
    public static PageReaderSubject Resolve(
        HttpContext context,
        PageSessionRequest session,
        VisitorOptions visitor,
        PageBrowserCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentNullException.ThrowIfNull(options);

        if (context.Items[s_itemKey] is PageReaderSubject resolved) {
            return resolved;
        }

        PageReaderSubject reader = new(session, visitor, PageBrowserCookie.Read(context, options));
        context.Items[s_itemKey] = reader;

        return reader;
    }
}
