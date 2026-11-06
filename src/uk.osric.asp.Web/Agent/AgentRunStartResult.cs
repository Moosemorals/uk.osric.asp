// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

/// <summary>Why a request to start a run did or did not start one.</summary>
internal enum AgentRunStartStatus {
    Started,
    NotFound,
    AlreadyActive,
}

/// <summary>The outcome of asking to start a run.</summary>
internal sealed record AgentRunStartResult(AgentRunStartStatus Status, string? RunId, long? UserMessageSequence);
