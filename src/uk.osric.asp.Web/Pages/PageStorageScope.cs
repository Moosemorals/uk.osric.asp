// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One page's <see cref="IPageStorage"/>, bound to that page's key.</summary>
/// <remarks>
/// <para>
/// The only handle a page ever gets, so it cannot name another page: isolation is structural rather
/// than a check. Keys and values are validated here, and every per-page cap is enforced by the store
/// in the same statement as the write.
/// </para>
/// <para>
/// A page's task and the abandoning thread no longer share a thread, so the abandoned flag is read
/// with <see cref="Volatile"/> rather than relying on the body lock.
/// </para>
/// </remarks>
internal sealed class PageStorageScope(
    PagePath path,
    IPageStateStore store,
    PageStorageOptions options,
    bool writable) : IRenderBoundPageStorage {

    // One instance is enough: JsonSerializerOptions is thread-safe once it has been used, and every
    // page serialises the same way.
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() },
    };

    private int _abandoned;
    private CancellationToken _deadline;

    /// <inheritdoc/>
    public void AttachDeadline(CancellationToken deadline) => _deadline = deadline;

    /// <inheritdoc/>
    public void Abandon() => Volatile.Write(ref _abandoned, 1);

    /// <inheritdoc/>
    public async Task<PageValue<T>> TryGetValueAsync<T>(string key) {
        ValidateKey(key);
        ThrowIfEnded();

        string? value = await store.GetValueAsync(path, key, Cancellation());
        if (value is null) {
            return new PageValue<T>(false, default);
        }

        // A stored JSON null is a value, not an absence, so the page can tell the two apart.
        return new PageValue<T>(true, JsonSerializer.Deserialize<T>(value, s_json));
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        string json = JsonSerializer.Serialize(value, s_json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > options.MaxValueBytes) {
            throw new ArgumentException(
                $"The value for {key} is larger than the {options.MaxValueBytes} byte limit.", nameof(value));
        }

        return store.SetValueAsync(path, key, json, Cancellation());
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key) {
        ValidateKey(key);
        ThrowIfReadOnly();
        ThrowIfEnded();

        return store.DeleteValueAsync(path, key, Cancellation());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> KeysAsync() {
        ThrowIfEnded();

        return store.ListKeysAsync(path, Cancellation());
    }

    private void ValidateKey(string key) {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (key.Length > options.MaxKeyChars) {
            throw new ArgumentException(
                $"A key may be at most {options.MaxKeyChars} characters.", nameof(key));
        }

        foreach (char character in key) {
            if (char.IsControl(character)) {
                throw new ArgumentException("A key may not contain a control character.", nameof(key));
            }
        }
    }

    // HEAD runs the page to produce the representation and its validators, so a write would count a
    // HEAD as a real visit - or mutate a remote system - once per validator.
    private void ThrowIfReadOnly() {
        if (!writable) {
            throw new InvalidOperationException($"{path.Key} may not write state during a HEAD request.");
        }
    }

    private void ThrowIfEnded() {
        if (Volatile.Read(ref _abandoned) != 0) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot use storage.");
        }

        if (_deadline.IsCancellationRequested) {
            throw new OperationCanceledException(_deadline);
        }
    }

    // The render's deadline, linked in by the renderer. The per-command budget is a command timeout on
    // the store's connection, not a second token.
    private CancellationToken Cancellation() => _deadline;
}
