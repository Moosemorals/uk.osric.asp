// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One object in <c>StoreObjects</c>: a subject's bytes, addressed by their digest.</summary>
/// <remarks>
/// The primary key is <c>(Subject, Sha)</c>, so content addressing is the dedupe: the same bytes put
/// twice under one subject are one row, and a put replaces the lifetime the previous row carried. The
/// subject is the binding — <c>page:</c>, <c>account:</c>, <c>session:</c> or <c>browser:</c> plus the
/// key — and a page-subject row is public, which is what <c>/o/{sha}</c> serves.
/// </remarks>
internal sealed class StoreObjectEntry {
    public required string Subject { get; init; }

    /// <summary>The lowercase hexadecimal SHA-256 digest the bytes are addressed by.</summary>
    public required string Sha { get; init; }

    /// <summary>The bytes' length, kept so a <c>HEAD</c> need not read the blob.</summary>
    public required long Length { get; init; }

    /// <summary>One of the store's fixed content types, in its canonical spelling.</summary>
    public required string ContentType { get; init; }

    /// <summary>The stored <c>StoreLifetimeKind</c>; <c>Page</c>, <c>Person</c>, <c>Session</c> or <c>Browser</c>.</summary>
    public required int Lifetime { get; init; }

    /// <summary>Fixed-width UTC ISO-8601, or <see langword="null"/> when there is no expiry.</summary>
    public string? ExpiresAt { get; init; }

    /// <summary>The bytes themselves.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Fixed-width UTC ISO-8601; the browser sweep reads it.</summary>
    public required string CreatedAt { get; init; }
}
