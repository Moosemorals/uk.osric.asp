// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One thing a running worker published.</summary>
/// <param name="Sequence">
/// The event's position in this job's journal, counting from one. A viewer passes the last sequence it
/// saw back to <see cref="IPageJobs.ReadEventsAsync"/>; the gap between them is never wider than the
/// journal, and a viewer whose position has been dropped is told so.
/// </param>
/// <param name="Type">The name the worker gave the event, such as <c>delta</c> or <c>done</c>.</param>
/// <param name="Data">The event's text or JSON, exactly as the worker published it.</param>
public sealed record PageJobEvent(long Sequence, string Type, string Data);
