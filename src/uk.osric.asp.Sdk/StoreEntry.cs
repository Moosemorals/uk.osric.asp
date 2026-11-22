// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One stored key: the page that wrote it, the key and the lifetime it follows.</summary>
/// <param name="Page">The canonical key of the page that wrote the entry.</param>
/// <param name="Key">The entry's key, which is ordinal and case-sensitive.</param>
/// <param name="Lifetime">The rule that decides when the entry dies.</param>
public readonly record struct StoreEntry(string Page, string Key, StoreLifetime Lifetime) {

    /// <summary>When the entry dies whatever else is true, or <see langword="null"/>.</summary>
    public DateTimeOffset? ExpiresAt => Lifetime.ExpiresAt;
}
