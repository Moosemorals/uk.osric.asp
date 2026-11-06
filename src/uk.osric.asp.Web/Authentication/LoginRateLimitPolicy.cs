// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Globalization;
using System.Net;

/// <summary>Names the rate-limiting policy applied to the sign-in POST.</summary>
internal static class LoginRateLimitPolicy {

    /// <summary>The policy name used by routing and the limiter registration.</summary>
    public const string Name = "login";

    // The count is defined as a shift so it cannot stop being a power of two: a literal could be
    // edited to something the mask below no longer describes, silently shrinking the bucket range.
    private const int BucketBits = 10;

    /// <summary>How many independent counters client addresses are folded into.</summary>
    /// <remarks>
    /// A power of two, so <see cref="BucketMask"/> can mask where a modulo would divide. 1024
    /// buckets bound the limiter's dictionary.
    /// </remarks>
    public const int Buckets = 1 << BucketBits;

    private const uint BucketMask = Buckets - 1;

    // FNV-1a. Any stable hash does, but not string.GetHashCode, which is randomised per process.
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>Names the bucket a client's attempts are counted in.</summary>
    /// <remarks>
    /// The limiter keeps a counter per key and never releases one, so the key cannot be an address
    /// the client chooses: folding addresses into a fixed set keeps that dictionary at a known size.
    /// Two clients share a bucket now and then, which is the price of the bound.
    /// </remarks>
    /// <param name="address">The client's address, or null when the server could not supply one.</param>
    /// <returns>The bucket name, stable for an address.</returns>
    public static string PartitionFor(IPAddress? address) {
        if (address is null) {
            return "unknown";
        }

        uint hash = FnvOffsetBasis;
        foreach (byte octet in address.GetAddressBytes()) {
            hash = (hash ^ octet) * FnvPrime;
        }

        return (hash & BucketMask).ToString(CultureInfo.InvariantCulture);
    }
}
