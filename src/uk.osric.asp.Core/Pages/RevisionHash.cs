// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

/// <summary>Creates stable addresses for individual saved page revisions.</summary>
public static class RevisionHash {

    /// <summary>The length of every stored hash: 32 SHA-256 bytes in lowercase hexadecimal.</summary>
    public const int Length = 64;

    /// <summary>The versioned framing prefix used by the revision identity format.</summary>
    internal const string FormatVersion = "page-revision-v1";

    /// <summary>Whether a value is exactly the lowercase hexadecimal hash storage writes.</summary>
    /// <param name="hash">The value to test.</param>
    /// <returns>
    /// True only for 64 lowercase hexadecimal characters. Case folding, truncation and padding are
    /// all refused, so a URL that names a revision has one spelling and one ordinal comparison.
    /// </returns>
    public static bool IsCanonical(string? hash) =>
        hash is { Length: Length } && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Hashes the source and immutable identity fields for a saved revision.</summary>
    /// <param name="source">The exact UTF-8 source stored in the revision.</param>
    /// <param name="creationRuntimeId">The runtime id captured when the revision was saved.</param>
    /// <param name="savedAt">The save instant, normalized to UTC for encoding.</param>
    /// <param name="revisionId">The unique shell-generated identifier for this save.</param>
    /// <returns>A lowercase 64-character SHA-256 digest.</returns>
    public static string Of(string source, string creationRuntimeId, DateTimeOffset savedAt, string revisionId) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(creationRuntimeId);
        ArgumentNullException.ThrowIfNull(revisionId);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, Encoding.UTF8.GetBytes(FormatVersion));
        AppendField(hash, Encoding.UTF8.GetBytes(source));
        AppendField(hash, Encoding.UTF8.GetBytes(creationRuntimeId));

        Span<byte> timestamp = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(timestamp, savedAt.UtcDateTime.Ticks);
        AppendField(hash, timestamp);

        AppendField(hash, Encoding.UTF8.GetBytes(revisionId));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> value) {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
