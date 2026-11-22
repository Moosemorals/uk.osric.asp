// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The obsolete <see cref="IPageSession"/> adapter over the Visitor subject.</summary>
/// <remarks>
/// It keeps the old asymmetry for one release: it sees and writes only
/// <see cref="StoreLifetimeKind.Session"/> values, so a page that has not been edited cannot suddenly
/// read a browser or person value. Signing out still ends its values, because a session value is
/// deleted with the session.
/// </remarks>
[Obsolete("Use ctx.Stores.Visitor.Values with StoreLifetime.Session or StoreLifetime.Browser. This spelling is removed one release after the storage model lands.")]
internal sealed class PageSessionScope(PageValueStore values) : IPageSession {

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key) => values.TryGetValueAsync<T>(key);

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string pagePath, string key) => values.TryGetValueAsync<T>(pagePath, key);

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value) => values.SetAsync(key, value, StoreLifetime.Session);

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key) => values.DeleteAsync(key);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> KeysAsync() {
        IReadOnlyList<StoreEntry> entries = await values.KeysAsync();

        return [.. entries.Select(entry => entry.Key)];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageSessionKey>> AllKeysAsync() {
        IReadOnlyList<StoreEntry> entries = await values.AllKeysAsync();

        return [.. entries.Select(entry => new PageSessionKey(entry.Page, entry.Key))];
    }
}
