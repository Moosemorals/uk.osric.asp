// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One key of one page's state, with its value as the stored JSON text.</summary>
/// <param name="Key">The key, 1–<see cref="PageStorageOptions.MaxKeyChars"/> characters, no control characters.</param>
/// <param name="Value">The value as JSON. A stored <c>null</c> is the text <c>null</c>, not a missing row.</param>
internal sealed record PageStateValue(string Key, string Value);
