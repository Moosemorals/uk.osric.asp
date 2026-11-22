// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One value in <c>StoreValues</c>: a subject, the page that wrote it, and a lifetime.</summary>
/// <remarks>
/// The subject is the binding — <c>page:</c>, <c>account:</c>, <c>session:</c> or <c>browser:</c> plus
/// the key — and the lifetime is the rule the entry follows. A key has one value and one lifetime at a
/// time; the read resolves the subject, not a lifetime.
/// </remarks>
internal sealed class StoreValueEntry {
    public required string Subject { get; init; }

    /// <summary>The canonical key of the page that wrote the entry.</summary>
    public required string PageKey { get; init; }

    public required string Key { get; init; }

    /// <summary>The stored <c>StoreLifetimeKind</c>; never <c>Default</c>, which the write resolves.</summary>
    public required int Lifetime { get; init; }

    /// <summary>Fixed-width UTC ISO-8601, or <see langword="null"/> when there is no expiry.</summary>
    public string? ExpiresAt { get; init; }

    /// <summary>The value as JSON. A stored <c>null</c> is the text <c>null</c>, not a missing row.</summary>
    public required string Value { get; set; }

    /// <summary>Fixed-width UTC ISO-8601; the browser sweep and the sampler read it.</summary>
    public required string UpdatedAt { get; init; }
}
