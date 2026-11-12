// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text;
using System.Text.Json;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;

/// <summary>One request's session id and whether its row exists yet.</summary>
/// <remarks>
/// The page endpoint resolves this once and every layer of the chain shares it, so two layers cannot
/// see different sessions. A row is minted on the first write rather than on the request, which is
/// what keeps a page that never uses the session from costing a visitor a cookie or a row.
/// </remarks>
internal sealed class PageSessionRequest(string id, string? account, bool presented, bool exists) {

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _touched;

    /// <summary>The value the cookie carries, whether the row exists yet or not.</summary>
    public string Id { get; } = id;

    /// <summary>The normalised account name of the request, or <see langword="null"/>.</summary>
    public string? Account { get; } = account;

    /// <summary>Whether the visitor presented this id in a cookie.</summary>
    public bool Presented { get; } = presented;

    /// <summary>Whether the session row exists.</summary>
    public bool Exists { get; private set; } = exists;

    /// <summary>Whether this request created the row.</summary>
    public bool Minted { get; private set; }

    /// <summary>Whether the response has to carry a cookie so the browser learns the new id.</summary>
    public bool NeedsCookie => Minted && !Presented;

    /// <summary>Mints the row if this is the request's first write.</summary>
    /// <param name="store">Where the row lives.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentException">The site has no room for another session.</exception>
    public async Task EnsureAsync(IPageSessionStore store, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(store);

        if (Exists) {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try {
            if (Exists) {
                return;
            }

            if (!await store.EnsureAsync(Id, Account, cancellationToken)) {
                throw new ArgumentException($"The site has no room for a new session for {Account ?? "an anonymous visitor"}.");
            }

            Exists = true;
            Minted = true;
        } finally {
            _ = _gate.Release();
        }
    }

    /// <summary>Slides the idle timer, at most once per request.</summary>
    /// <param name="store">Where the row lives.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public async Task TouchAsync(IPageSessionStore store, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(store);

        if (!Exists || _touched) {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try {
            if (!Exists || _touched) {
                return;
            }

            await store.TouchAsync(Id, cancellationToken);
            _touched = true;
        } finally {
            _ = _gate.Release();
        }
    }

    /// <summary>Reads a string the shell wrote.</summary>
    /// <param name="store">Where the value lives.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored string, or <see langword="null"/> when the key is absent.</returns>
    public async Task<string?> GetStringAsync(
        IPageSessionStore store,
        string pagePath,
        string key,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(store);

        string? json = await store.GetValueAsync(Id, pagePath, key, cancellationToken);

        return json is null ? null : JsonSerializer.Deserialize<string>(json);
    }

    /// <summary>Writes a value for the shell's own use, minting the session when it does not exist.</summary>
    /// <param name="store">Where the value lives.</param>
    /// <param name="options">The caps the session obeys.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="text">The text to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task SetTextAsync(
        IPageSessionStore store,
        PageSessionOptions options,
        string pagePath,
        string key,
        string text,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        PageStateKey.Validate(key, options.MaxKeyChars);

        string json = JsonSerializer.Serialize(text);
        PageSessionCaps caps = options.For(Account);
        if (Encoding.UTF8.GetByteCount(json) > caps.MaxValueBytes) {
            throw new ArgumentException($"The value for {key} is larger than the {caps.MaxValueBytes} byte limit.", nameof(text));
        }

        await EnsureAsync(store, cancellationToken);
        await store.SetValueAsync(Id, pagePath, key, json, caps, cancellationToken);
    }

    /// <summary>Removes a value the shell wrote.</summary>
    /// <param name="store">Where the value lives.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the key existed.</returns>
    public Task<bool> DeleteTextAsync(
        IPageSessionStore store,
        string pagePath,
        string key,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(store);

        return store.DeleteValueAsync(Id, pagePath, key, cancellationToken);
    }
}

/// <summary>Resolves the request's session once, before any layer sees it.</summary>
internal static class PageSessionResolver {

    private static readonly object s_itemKey = new();

    /// <summary>Finds, discards or starts the session for this request.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="store">Where session rows live.</param>
    /// <param name="options">The cookie's name.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The resolved session, cached on the request so every caller sees one.</returns>
    /// <remarks>
    /// A row that belongs to an account is only honoured for that account: a stale cookie cannot
    /// inherit the account's caps or read what an owner's page left behind. A mismatch deletes the
    /// row and starts a fresh session.
    /// </remarks>
    public static async Task<PageSessionRequest> ResolveAsync(
        HttpContext context,
        IPageSessionStore store,
        PageSessionCookieOptions options,
        PageUser? user,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        if (context.Items[s_itemKey] is PageSessionRequest resolved) {
            return resolved;
        }

        PageSessionRequest request = await ResolveCoreAsync(context, store, options, user, cancellationToken);
        context.Items[s_itemKey] = request;

        return request;
    }

    private static async Task<PageSessionRequest> ResolveCoreAsync(
        HttpContext context,
        IPageSessionStore store,
        PageSessionCookieOptions options,
        PageUser? user,
        CancellationToken cancellationToken) {
        string? presented = PageSessionCookie.Read(context, options);
        if (presented is null) {
            return new PageSessionRequest(PageSessionCookie.Mint(), user?.Name, presented: false, exists: false);
        }

        PageSessionEntry? row = await store.FindAsync(presented, cancellationToken);
        if (row is null) {
            // The row is gone, but the browser's id is as good as any other and reusing it keeps the
            // cookie the visitor already has.
            return new PageSessionRequest(presented, user?.Name, presented: true, exists: false);
        }

        if (row.Account is not null && !string.Equals(row.Account, user?.Name, StringComparison.Ordinal)) {
            _ = await store.DeleteAsync(presented, cancellationToken);
            return new PageSessionRequest(PageSessionCookie.Mint(), user?.Name, presented: false, exists: false);
        }

        return new PageSessionRequest(presented, user?.Name, presented: true, exists: true);
    }
}
