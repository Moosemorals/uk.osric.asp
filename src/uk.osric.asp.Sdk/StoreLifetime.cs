// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>How long one stored entry lives, and when it expires.</summary>
/// <remarks>
/// <para>
/// A read answers the lifetime the entry was stored with, so a page can tell a shared value from one
/// that follows the reader. A write without a lifetime takes the subject's own: <see cref="Page"/>
/// on a Page subject, <see cref="Person"/> on a Visitor.
/// </para>
/// <para>
/// <see cref="Until(DateTimeOffset)"/> composes with the default rather than replacing it; use
/// <see cref="WithExpiry(DateTimeOffset)"/> to put an expiry on a named lifetime.
/// </para>
/// </remarks>
public readonly record struct StoreLifetime {

    private StoreLifetime(StoreLifetimeKind kind, DateTimeOffset? expiresAt) {
        Kind = kind;
        ExpiresAt = expiresAt;
    }

    /// <summary>The subject's own rule: <see cref="Page"/> on a Page subject, <see cref="Person"/> on a Visitor.</summary>
    public static StoreLifetime Default => new(StoreLifetimeKind.Default, null);

    /// <summary>Dies when the page goes.</summary>
    public static StoreLifetime Page => new(StoreLifetimeKind.Page, null);

    /// <summary>Dies when the account goes; anonymously, when the session does, unless adoption moves it.</summary>
    public static StoreLifetime Person => new(StoreLifetimeKind.Person, null);

    /// <summary>Dies when the session rotates, or when the identity row is swept.</summary>
    public static StoreLifetime Session => new(StoreLifetimeKind.Session, null);

    /// <summary>Dies when the browser's cookie expires or is cleared.</summary>
    public static StoreLifetime Browser => new(StoreLifetimeKind.Browser, null);

    /// <summary>Dies at the next successful take of that key.</summary>
    public static StoreLifetime NextRead => new(StoreLifetimeKind.NextRead, null);

    /// <summary>A lifetime that also expires at a chosen time.</summary>
    /// <param name="expiry">When the entry dies, whatever the subject's default is.</param>
    /// <exception cref="ArgumentOutOfRangeException">No expiry was given.</exception>
    public static StoreLifetime Until(DateTimeOffset expiry) => new(StoreLifetimeKind.Default, Valid(expiry));

    /// <summary>This lifetime with an expiry, so a named rule can also time out.</summary>
    /// <param name="expiry">When the entry dies.</param>
    /// <returns>The same kind, with the expiry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No expiry was given.</exception>
    public StoreLifetime WithExpiry(DateTimeOffset expiry) => new(Kind, Valid(expiry));

    /// <summary>The lifetime a read answered, rebuilt from its kind and expiry.</summary>
    /// <param name="kind">The rule that decided the entry's life.</param>
    /// <param name="expiresAt">The expiry the entry also carried, or <see langword="null"/>.</param>
    /// <returns>The lifetime a read reports.</returns>
    public static StoreLifetime From(StoreLifetimeKind kind, DateTimeOffset? expiresAt = null) => new(kind, expiresAt);

    /// <summary>Which rule decides the entry's life.</summary>
    public StoreLifetimeKind Kind { get; }

    /// <summary>When the entry dies whatever else is true, or <see langword="null"/> when nothing else applies.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    private static DateTimeOffset Valid(DateTimeOffset expiry) =>
        expiry == default
            ? throw new ArgumentOutOfRangeException(nameof(expiry), "An expiry is a real time, not the default.")
            : expiry;
}
