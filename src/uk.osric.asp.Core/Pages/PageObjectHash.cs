// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using System.Security.Cryptography;

/// <summary>The SHA-256 digest that addresses an object, and the one spelling of it a URL may name.</summary>
/// <remarks>
/// The digest is the object's identity: the same bytes under one subject are one row, and the route
/// serves them under this value. Case folding, truncation and padding are refused for the same reason
/// a revision hash refuses them, so a URL that names an object has one spelling and one comparison.
/// </remarks>
public static class PageObjectHash {

    /// <summary>The length of every digest: 32 SHA-256 bytes in lowercase hexadecimal.</summary>
    public const int Length = 64;

    /// <summary>Hashes the bytes an object holds.</summary>
    /// <param name="content">The bytes; never null.</param>
    /// <returns>The lowercase, 64-character SHA-256 digest.</returns>
    /// <exception cref="ArgumentNullException">The content is null.</exception>
    public static string Of(byte[] content) {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>Whether a value is exactly the lowercase hexadecimal SHA-256 storage writes.</summary>
    /// <param name="sha">The value to test.</param>
    /// <returns>True only for 64 lowercase hexadecimal characters.</returns>
    public static bool IsCanonical(string? sha) =>
        sha is { Length: Length } && sha.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
