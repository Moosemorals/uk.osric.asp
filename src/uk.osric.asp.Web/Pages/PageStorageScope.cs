// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The obsolete <see cref="IPageStorage"/> adapter over the Page subject.</summary>
/// <remarks>
/// A stored page that has not been edited keeps working for one release, against
/// <c>ctx.Stores.Page.Values</c>. The adapter is a window, not a home: the manual and the prompt
/// steer to <c>ctx.Stores</c>, and the removal is a follow-up release.
/// </remarks>
[Obsolete("Use ctx.Stores.Page.Values. This spelling is removed one release after the storage model lands.")]
internal sealed class PageStorageScope(PageValueStore values) : IPageStorage {

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key) => values.TryGetValueAsync<T>(key);

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value) => values.SetAsync(key, value, StoreLifetime.Page);

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key) => values.DeleteAsync(key);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> KeysAsync() {
        IReadOnlyList<StoreEntry> entries = await values.KeysAsync();

        return [.. entries.Select(entry => entry.Key)];
    }
}
