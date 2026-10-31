// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Security.Cryptography;
using System.Text;

/// <summary>Identifies one version of a page's source by its content.</summary>
/// <remarks>
/// <para>
/// A stored compiled image is only reused while its fingerprint still matches the source it was
/// built from, so an edit that bypasses the editor cannot be answered with stale code.
/// </para>
/// <para>
/// The value is an opaque token compared with a freshly computed one and never parsed, so it carries
/// no algorithm marker. Changing the algorithm changes this assembly, which changes the runtime id
/// stored beside every image; each image stops matching and is compiled again. Hexadecimal is chosen
/// over base64 for a single canonical form, for use in a URL or an entity tag later without
/// escaping, and for readability in the database. The 21 bytes per page that base64 would save do
/// not matter at this size.
/// </para>
/// </remarks>
public static class ContentFingerprint {

    /// <summary>Fingerprints page content.</summary>
    /// <param name="content">The page source.</param>
    /// <returns>The lowercase hexadecimal SHA-256 of the content's UTF-8 bytes.</returns>
    public static string Of(string content) {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
