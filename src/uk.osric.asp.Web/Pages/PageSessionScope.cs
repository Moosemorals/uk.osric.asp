// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One page's <see cref="IPageSession"/>, bound to that page's key.</summary>
/// <remarks>
/// Isolation is structural rather than a check: every write, delete and listing uses the page's own
/// path, and there is no overload that names another. Reading is open, because a page showing what
/// another page left it is the point; the path is normalised before use, so a traversal cannot name
/// something else.
/// </remarks>
internal sealed class PageSessionScope(
    PagePath path,
    IPageSessionStore store,
    PageSessionOptions options,
    PageSessionRequest session,
    bool writable,
    bool slides) : RenderBoundCapability, IPageSession {

    // One instance is enough: JsonSerializerOptions is thread-safe once it has been used, and every
    // page serialises the same way page storage does.
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key) => ReadAsync<T>(path.Key, key);

    /// <inheritdoc/>
    public Task<PageValue<T>> TryGetValueAsync<T>(string pagePath, string key) {
        PagePath other = PagePath.Normalize(pagePath);

        if (!other.IsAddressable) {
            throw new ArgumentException($"{other} cannot name a page.", nameof(pagePath));
        }

        return ReadAsync<T>(other.Key, key);
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        string json = JsonSerializer.Serialize(value, s_json);
        PageSessionCaps caps = options.For(session.Account);
        if (Encoding.UTF8.GetByteCount(json) > caps.MaxValueBytes) {
            throw new ArgumentException(
                $"The value for {key} is larger than the {caps.MaxValueBytes} byte limit.", nameof(value));
        }

        await session.EnsureAsync(store, Cancellation());
        await TouchAsync();
        await store.SetValueAsync(session.Id, path.Key, key, json, caps, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        await TouchAsync();

        return await store.DeleteValueAsync(session.Id, path.Key, key, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> KeysAsync() {
        ThrowIfEnded();
        await TouchAsync();

        return await store.ListKeysAsync(session.Id, path.Key, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageSessionKey>> AllKeysAsync() {
        ThrowIfEnded();
        await TouchAsync();

        return await store.ListAllKeysAsync(session.Id, Cancellation());
    }

    private async Task<PageValue<T>> ReadAsync<T>(string pagePath, string key) {
        ValidateKey(key);
        ThrowIfEnded();
        await TouchAsync();

        string? value = await store.GetValueAsync(session.Id, pagePath, key, Cancellation());
        if (value is null) {
            return new PageValue<T>(false, default);
        }

        // A stored JSON null is a value, not an absence, so the page can tell the two apart.
        return new PageValue<T>(true, JsonSerializer.Deserialize<T>(value, s_json));
    }

    private Task TouchAsync() => slides ? session.TouchAsync(store, Cancellation()) : Task.CompletedTask;

    private void ValidateKey(string key) => PageStateKey.Validate(key, options.MaxKeyChars);

    // HEAD runs the page to produce the representation, and a layout is markup: neither may write
    // state on behalf of the visitor who is reading.
    private void ThrowIfReadOnly() {
        if (!writable) {
            throw new InvalidOperationException($"{path.Key} may not write its session state during this render.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot use the session.");
        }

        Deadline.ThrowIfCancellationRequested();
    }

    // The render's deadline, linked in by the renderer. The per-command budget is a command timeout on
    // the store's connection, not a second token.
    private CancellationToken Cancellation() => Deadline;
}
