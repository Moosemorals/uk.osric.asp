// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What became of a write: durable, staged, or refused.</summary>
/// <param name="Outcome">Whether the write is durable, staged, or was refused.</param>
/// <remarks>
/// The rows a statement changed are not part of the answer: a write reaches a declared table through
/// a view, and SQLite counts nothing a view's trigger changed, so a page that needs a count reads it
/// back with a statement.
/// </remarks>
public readonly record struct PageRecordResult(PageRecordOutcome Outcome);
