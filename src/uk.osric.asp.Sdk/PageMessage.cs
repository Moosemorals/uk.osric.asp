// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One message waiting for the reader.</summary>
/// <param name="From">The canonical key of the page, library or worker that posted it, stamped by the host.</param>
/// <param name="Body">The text the poster wrote.</param>
/// <param name="SentAt">When it was queued, in UTC.</param>
/// <remarks>
/// The owner is not part of the value: a message belongs to the reader who takes it, and the take
/// already filtered by that subject.
/// </remarks>
public sealed record PageMessage(string From, string Body, DateTimeOffset SentAt);
