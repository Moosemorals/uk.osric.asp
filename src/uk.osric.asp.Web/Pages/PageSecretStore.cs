// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The Page subject's <see cref="ISecretStore"/>, bound to the layer's page.</summary>
/// <remarks>
/// The handle answers its own key's secrets and nothing else: there is no page argument, so a page
/// cannot name another page's secret even by mistake. A read is allowed during a <c>HEAD</c>; a set and
/// a delete are writes and are refused there, and during an abandoned render or a passed deadline.
/// </remarks>
internal sealed class PageSecretStore : RenderBoundCapability, ISecretStore {

    private readonly PagePath _bound;
    private readonly IStoreSecrets _store;
    private readonly PageSecretOptions _limits;
    private readonly bool _writable;
    private readonly Func<CancellationToken, Task>? _validate;

    /// <summary>Creates the layer's secret handle.</summary>
    /// <param name="bound">The layer's own key.</param>
    /// <param name="store">Where secrets live.</param>
    /// <param name="limits">The secret shape's limits.</param>
    /// <param name="writable">Whether this render may write.</param>
    /// <param name="validate">Checks a <c>For</c> key on first use, or <see langword="null"/>.</param>
    public PageSecretStore(
        PagePath bound,
        IStoreSecrets store,
        PageSecretOptions limits,
        bool writable,
        Func<CancellationToken, Task>? validate = null) {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);

        _bound = bound;
        _store = store;
        _limits = limits;
        _writable = writable;
        _validate = validate;
    }

    /// <inheritdoc/>
    public async Task<string?> TryGetAsync(string name) {
        ValidateName(name);
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());

        StoreSecretRow? row = await _store.FindAsync(Subject, name, Cancellation());
        return row?.Value;
    }

    /// <inheritdoc/>
    public async Task SetAsync(string name, string value) {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrEmpty(value);

        long bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > _limits.MaxSecretBytes) {
            throw new ArgumentException(
                $"The secret {name} is {bytes} bytes and Pages:Stores:Secrets:MaxSecretBytes is {_limits.MaxSecretBytes}.",
                nameof(value));
        }

        ThrowIfReadOnly();
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());

        await _store.WriteAsync(
            new StoreSecretWrite(Subject, _bound.Key, name, value, RequireLivePage: true),
            _limits,
            Cancellation());
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string name) {
        ValidateName(name);
        ThrowIfReadOnly();
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());

        return await _store.DeleteAsync(Subject, name, Cancellation());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> NamesAsync() {
        ThrowIfEnded();
        await ConfirmedAsync(Cancellation());

        IReadOnlyList<StoreSecretRow> rows =
            await _store.ListAsync(Subject, _limits.MaxSecrets, Cancellation());

        return [.. rows.Select(row => row.Name)];
    }

    private string Subject => StoreSubjectKeys.ForPage(_bound);

    private void ValidateName(string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.Length > _limits.MaxNameChars) {
            throw new ArgumentException(
                $"A secret name may be at most {_limits.MaxNameChars} characters.", nameof(name));
        }

        foreach (char character in name) {
            if (char.IsControl(character)) {
                throw new ArgumentException("A secret name may not contain a control character.", nameof(name));
            }
        }
    }

    // For's proof that the key is a live library or worker runs on first use, and the caller's
    // delegate memoises the read so every later call answers at once.
    private async Task ConfirmedAsync(CancellationToken cancellationToken) {
        if (_validate is not null) {
            await _validate(cancellationToken);
        }
    }

    private void ThrowIfReadOnly() {
        if (!_writable) {
            throw new InvalidOperationException($"{_bound.Key} may not write a secret during a HEAD request.");
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
