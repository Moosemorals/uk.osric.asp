// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

/// <summary>One journaled event of a run, as the assistant page replays it.</summary>
internal sealed record AgentEvent(string RunId, long Sequence, string Type, string DataJson) {
    public string EventId => $"{RunId}:{Sequence}";
}
