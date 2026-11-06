// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>One event emitted while a provider streams a completion.</summary>
public abstract record ChatStreamEvent;

/// <summary>A fragment of assistant response text.</summary>
public sealed record ContentDelta(string Text) : ChatStreamEvent;

/// <summary>A fragment of provider-supplied reasoning, kept display-only by the host.</summary>
public sealed record ReasoningDelta(string Text) : ChatStreamEvent;

/// <summary>A fragment of one indexed tool call.</summary>
public sealed record ToolCallDelta(int Index, string? Id, string? Name, string? Arguments) : ChatStreamEvent;

/// <summary>A usage event from the provider.</summary>
public sealed record UsageEvent(TokenUsage Usage) : ChatStreamEvent;

/// <summary>The provider's completion reason for a stream step.</summary>
public sealed record Finished(string? Reason) : ChatStreamEvent;
