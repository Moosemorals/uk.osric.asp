// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One message in <c>StoreMessages</c>: a queue, a sender and the reader it belongs to.</summary>
/// <remarks>
/// Exactly one of <see cref="ToPageKey"/> and <see cref="Topic"/> is set. The subject is the reader's
/// — an account, a session or a browser — so the take filters by it and the row needs no second pair
/// of owner columns. <see cref="Id"/> is the FIFO order.
/// </remarks>
internal sealed class StoreMessageEntry {
    public long Id { get; init; }

    /// <summary>The reader's subject: <c>account:</c>, <c>session:</c> or <c>browser:</c> plus the key.</summary>
    public required string Subject { get; init; }

    /// <summary>The destination page's canonical key, or <see langword="null"/> for a topic.</summary>
    public string? ToPageKey { get; init; }

    /// <summary>The named queue, or <see langword="null"/> for a page address.</summary>
    public string? Topic { get; init; }

    /// <summary>The canonical key of the page, library or worker that posted it.</summary>
    public required string FromPageKey { get; init; }

    public required string Body { get; init; }

    /// <summary>The stored <c>StoreLifetimeKind</c>; <c>Person</c>, <c>Session</c> or <c>Browser</c>.</summary>
    public required int Lifetime { get; init; }

    /// <summary>Fixed-width UTC ISO-8601, or <see langword="null"/> when there is no expiry.</summary>
    public string? ExpiresAt { get; init; }

    /// <summary>Fixed-width UTC ISO-8601; the FIFO order is <see cref="Id"/>, not this.</summary>
    public required string SentAt { get; init; }
}
