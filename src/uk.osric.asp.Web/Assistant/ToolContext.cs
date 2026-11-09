// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>Who a tool call is for: the owner, the conversation and the run.</summary>
/// <remarks>
/// Memory is the first tool family that belongs to a person rather than to the site, so the executor
/// is told who is calling instead of reading it from the model's arguments. The owner comes from the
/// signed-in conversation and there is no path by which a model-supplied string becomes one (DEC-184).
/// </remarks>
/// <param name="Owner">The account the run belongs to.</param>
/// <param name="ConversationId">The conversation the run is in.</param>
/// <param name="RunId">The run making the call.</param>
internal sealed record ToolContext(string Owner, long ConversationId, string RunId) {

    /// <summary>Where a memory written by this call came from.</summary>
    public MemorySource Source => new(ConversationId, RunId);
}
