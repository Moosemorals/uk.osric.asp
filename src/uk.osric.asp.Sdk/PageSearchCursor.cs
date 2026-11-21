// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A position in a search walk: a key and the 1-based line a slice starts at.</summary>
/// <param name="Key">The canonical key.</param>
/// <param name="Line">The 1-based line the next slice tests first.</param>
/// <remarks>
/// A caller does not build one: it passes back the <see cref="PageSearch.Cursor"/> an answer carried.
/// The pair is the first position the answer did not include, so a walk neither repeats nor skips a
/// line, and a slice that found nothing still moves forward.
/// </remarks>
public readonly record struct PageSearchCursor(string Key, int Line);
