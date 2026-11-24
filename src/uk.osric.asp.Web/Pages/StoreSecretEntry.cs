// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One secret in <c>StoreSecrets</c>: a page's opaque text, by name.</summary>
/// <remarks>
/// The primary key is <c>(Subject, PageKey, Name)</c>, as <c>StoreValues</c>' is, but there is no
/// lifetime column and no expiry: a secret is the page's or it is not there. The subject is always a
/// page's, because the shape exists on the Page subject only.
/// </remarks>
internal sealed class StoreSecretEntry {
    public required string Subject { get; init; }

    /// <summary>The canonical key of the page that owns the secret.</summary>
    public required string PageKey { get; init; }

    public required string Name { get; init; }

    /// <summary>The text itself. Opaque: the store never parses it.</summary>
    public required string Value { get; set; }

    /// <summary>Fixed-width UTC ISO-8601; the storage figure reads it.</summary>
    public required string UpdatedAt { get; init; }
}
