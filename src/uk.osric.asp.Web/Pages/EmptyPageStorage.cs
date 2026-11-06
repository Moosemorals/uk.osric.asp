// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The storage a page gets before the real store is wired into the context.</summary>
/// <remarks>
/// Reads report every key absent and writes are refused outright, so a page cannot believe it stored
/// something the host threw away.
/// </remarks>
internal sealed class EmptyPageStorage : IPageStorage {

    /// <summary>The one instance, because it holds nothing.</summary>
    public static EmptyPageStorage Instance { get; } = new();

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Task.FromResult(new PageValue<T>(false, default));
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value) {
        throw new InvalidOperationException("Page storage is not available in this host.");
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key) {
        throw new InvalidOperationException("Page storage is not available in this host.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> KeysAsync() {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}
