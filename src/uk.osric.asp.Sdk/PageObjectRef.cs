// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A stored object's address, length and content type, without its bytes.</summary>
/// <param name="Sha">The lowercase hexadecimal SHA-256 digest the bytes are addressed by.</param>
/// <param name="Length">How many bytes the object holds.</param>
/// <param name="ContentType">The canonical content type from the store's allow-list.</param>
/// <remarks>
/// <c>/o/{sha}</c> is the URL the reference names, so a page can write a link or an <c>img</c> from
/// the reference a put answered without opening the bytes again.
/// </remarks>
public readonly record struct PageObjectRef(string Sha, long Length, string ContentType);
