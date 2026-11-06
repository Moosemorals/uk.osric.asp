// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

/// <summary>A complete provider tool call, ordered by its stream index.</summary>
public sealed record AgentToolCall(int Index, string? Id, string? Name, string Arguments);
