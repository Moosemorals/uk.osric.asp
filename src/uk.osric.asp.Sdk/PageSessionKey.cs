// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One key of one page's per-visitor session state.</summary>
/// <remarks>
/// A session value is addressed by the page that wrote it and the key that page chose. A bare key
/// cannot be read because two pages may use the same key for different things.
/// </remarks>
/// <param name="Page">The canonical page key the value belongs to.</param>
/// <param name="Key">The key that page chose.</param>
public readonly record struct PageSessionKey(string Page, string Key);
