// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

/// <summary>One head's facts, as a listing reads them.</summary>
/// <param name="Key">The page's canonical key, which orders the set.</param>
/// <param name="RevisionId">The revision the head points at.</param>
/// <param name="IsDeleted">Whether the head is a tombstone.</param>
/// <param name="SeedHash">The seed the page is anchored to, or <see langword="null"/> when unknown.</param>
/// <param name="ContentHash">The content hash of the head's source, or <see langword="null"/> when unknown.</param>
public readonly record struct PageFingerprintFact(
    string Key,
    string RevisionId,
    bool IsDeleted,
    string? SeedHash,
    string? ContentHash);

/// <summary>The fingerprint of every head under a prefix, so a listing can say the store moved.</summary>
/// <remarks>
/// <para>
/// A listing resumes from a key, so a caller can walk a store one slice at a time. The fingerprint
/// answers the other half: whether the set of rows it is walking is still the one it started on. It
/// is derived from the heads when a slice is read rather than kept in a column or a counter, so no
/// write path maintains it and a write nobody remembered to annotate still moves it.
/// </para>
/// <para>
/// Each field is written as a netstring — a decimal UTF-8 byte count, a colon, then the bytes — and
/// the facts are sorted by key with <see cref="StringComparer.Ordinal"/>, so no key, revision id or
/// hash can run into the field beside it. A tombstone is <c>1</c> and a live head is <c>0</c>; an
/// absent hash is an empty field. The answer is the lowercase hexadecimal SHA-256 of that byte
/// sequence; the empty set is the hash of no bytes, which is a stable token in its own right.
/// </para>
/// <para>
/// The value is opaque: compare it with a freshly computed one and never parse or order it. It
/// carries no algorithm marker, so changing the formula would make every token different, which
/// matters only to a walk that spans the deployment and is the safe direction.
/// </para>
/// </remarks>
public static class PageSubtreeFingerprint {

    /// <summary>Fingerprints a set of head facts.</summary>
    /// <param name="facts">Every head under one prefix, in any order.</param>
    /// <returns>The lowercase hexadecimal SHA-256 of the canonical form, or the empty set's hash for no facts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="facts"/> is <see langword="null"/>.</exception>
    public static string Of(IEnumerable<PageFingerprintFact> facts) {
        ArgumentNullException.ThrowIfNull(facts);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (PageFingerprintFact fact in facts.OrderBy(fact => fact.Key, StringComparer.Ordinal)) {
            Append(hash, fact.Key);
            Append(hash, fact.RevisionId);
            Append(hash, fact.IsDeleted ? "1" : "0");
            Append(hash, fact.SeedHash ?? string.Empty);
            Append(hash, fact.ContentHash ?? string.Empty);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value) {
        int length = Encoding.UTF8.GetByteCount(value);

        Span<byte> count = stackalloc byte[12];
        _ = Utf8Formatter.TryFormat(length, count, out int written);
        hash.AppendData(count[..written]);
        hash.AppendData(":"u8);

        // A key, a revision id and a hash all fit the stack buffer; a longer value rents rather than
        // allocating per field, so the hash stays a small part of the read it accompanies.
        if (length <= 256) {
            Span<byte> buffer = stackalloc byte[256];
            hash.AppendData(buffer[..Encoding.UTF8.GetBytes(value, buffer)]);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try {
            hash.AppendData(rented.AsSpan(0, Encoding.UTF8.GetBytes(value, rented)));
        } finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
