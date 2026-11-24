// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A page's secrets: opaque text, by name, that nothing else returns.</summary>
/// <remarks>
/// <para>
/// A secret is authority rather than data, and the page is its only reader. The handle answers its own
/// key's names and nothing else: there is no <c>pageKey</c> overload beside <see cref="TryGetAsync"/>,
/// so the open cross-page read a value allows stops here. A layout's handle is the layout's own key, so
/// a wrapper cannot read what the page it wraps keeps.
/// </para>
/// <para>
/// The signature is <see cref="string"/>, not JSON, so a page cannot be handed a structured secret by
/// accident. A secret has no lifetime: it is the page's or it is not there. <see cref="NamesAsync"/>
/// answers names so a page can offer rotation without the store handing out a value.
/// </para>
/// <para>
/// Delete, scrub and move take the secrets with the page, exactly as they take its values. An archive
/// carries neither a name nor a value, and an import that carries one is refused. The rows are text at
/// rest, the same boundary as a password hash or a session row; encryption is a separate item.
/// </para>
/// <para>
/// Every write is refused during a <c>HEAD</c>, an abandoned render or a passed deadline, and the limits
/// are checked in the statement that writes, so a concurrent writer either fits or is refused.
/// </para>
/// </remarks>
public interface ISecretStore {

    /// <summary>Reads one of the bound page's secrets by name.</summary>
    /// <param name="name">The name; ordinal, 1 to the configured maximum, no control characters.</param>
    /// <returns>The value, or <see langword="null"/> when the page holds no secret of that name.</returns>
    /// <exception cref="System.ArgumentException">The name breaks a rule.</exception>
    /// <exception cref="System.InvalidOperationException">The render has ended: an abandoned render or a passed deadline.</exception>
    public Task<string?> TryGetAsync(string name);

    /// <summary>Stores one of the bound page's secrets, replacing whatever the name held.</summary>
    /// <param name="name">The name; ordinal, 1 to the configured maximum, no control characters.</param>
    /// <param name="value">The text; never empty, and bounded by the name and subject limits.</param>
    /// <returns>A task that completes when the write is committed.</returns>
    /// <exception cref="System.ArgumentException">The name or the value breaks a rule or exceeds a limit.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task SetAsync(string name, string value);

    /// <summary>Removes one of the bound page's secrets.</summary>
    /// <param name="name">The name to remove.</param>
    /// <returns>Whether a secret was removed. Deleting a missing name is not an error.</returns>
    /// <exception cref="System.ArgumentException">The name breaks a rule.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<bool> DeleteAsync(string name);

    /// <summary>Lists the names the bound page holds, without a value or a preview.</summary>
    /// <returns>The names, ordered ordinally.</returns>
    /// <exception cref="System.InvalidOperationException">The render has ended: an abandoned render or a passed deadline.</exception>
    public Task<IReadOnlyList<string>> NamesAsync();
}
