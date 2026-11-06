// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

/// <summary>One event emitted while a provider streams a completion.</summary>
public abstract record AgentChatStreamEvent;

/// <summary>A fragment of assistant response text.</summary>
public sealed record AgentContentDelta(string Text) : AgentChatStreamEvent;

/// <summary>A fragment of provider-supplied reasoning, kept display-only by the host.</summary>
public sealed record AgentReasoningDelta(string Text) : AgentChatStreamEvent;

/// <summary>A fragment of one indexed tool call.</summary>
public sealed record AgentToolCallDelta(int Index, string? Id, string? Name, string? Arguments) : AgentChatStreamEvent;

/// <summary>A usage event from the provider.</summary>
public sealed record AgentUsageEvent(AgentUsage Usage) : AgentChatStreamEvent;

/// <summary>The provider's completion reason for a stream step.</summary>
public sealed record AgentFinished(string? Reason) : AgentChatStreamEvent;
