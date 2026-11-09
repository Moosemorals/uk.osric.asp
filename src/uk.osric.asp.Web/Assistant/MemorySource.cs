// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>Where a memory's current version came from: the conversation and the run that wrote it.</summary>
/// <remarks>
/// A record of both, so a caller cannot pass one and forget the other, and so the owner's page can link
/// back to the turn that wrote a memory while that conversation exists.
/// </remarks>
/// <param name="ConversationId">The conversation the write happened in.</param>
/// <param name="RunId">The run that made the call.</param>
internal sealed record MemorySource(long ConversationId, string RunId);
