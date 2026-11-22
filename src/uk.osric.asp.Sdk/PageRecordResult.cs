// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>How many rows a write changed, and what became of it.</summary>
/// <param name="Affected">The rows the statement changed; zero for <see cref="PageRecordOutcome.Busy"/> and <see cref="PageRecordOutcome.Full"/>.</param>
/// <param name="Outcome">Whether the write is durable, staged, or was refused.</param>
public readonly record struct PageRecordResult(int Affected, PageRecordOutcome Outcome);
