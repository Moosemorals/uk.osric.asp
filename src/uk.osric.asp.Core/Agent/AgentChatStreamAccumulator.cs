// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

using System.Text;

/// <summary>One event emitted while a provider streams a completion.</summary>
public abstract record AgentChatStreamEvent;

/// <summary>A fragment of assistant response text.</summary>
public sealed record AgentContentDelta(string Text) : AgentChatStreamEvent;

/// <summary>A fragment of provider-supplied reasoning, kept display-only by the host.</summary>
public sealed record AgentReasoningDelta(string Text) : AgentChatStreamEvent;

/// <summary>A fragment of one indexed tool call.</summary>
public sealed record AgentToolCallDelta(int Index, string? Id, string? Name, string? Arguments) : AgentChatStreamEvent;

/// <summary>Provider token counts, when available.</summary>
public sealed record AgentUsage(int? PromptTokens, int? CompletionTokens);

/// <summary>A usage event from the provider.</summary>
public sealed record AgentUsageEvent(AgentUsage Usage) : AgentChatStreamEvent;

/// <summary>The provider's completion reason for a stream step.</summary>
public sealed record AgentFinished(string? Reason) : AgentChatStreamEvent;

/// <summary>A complete provider tool call, ordered by its stream index.</summary>
public sealed record AgentToolCall(int Index, string? Id, string? Name, string Arguments);

/// <summary>The accumulated values from one provider response.</summary>
public sealed record AgentChatResponse(
    string Content,
    string Reasoning,
    IReadOnlyList<AgentToolCall> ToolCalls,
    string? FinishReason,
    AgentUsage? Usage) {

    /// <summary>Whether the provider emitted user-visible or actionable data.</summary>
    public bool HasContent => Content.Length > 0 || Reasoning.Length > 0 || ToolCalls.Count > 0;
}

/// <summary>Combines provider deltas into one complete assistant response.</summary>
public sealed class AgentChatStreamAccumulator {
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly Dictionary<int, ToolCallBuilder> _toolCalls = [];
    private string? _finishReason;
    private AgentUsage? _usage;

    /// <summary>Adds one provider event to the current response.</summary>
    /// <param name="event">The provider event.</param>
    public void Add(AgentChatStreamEvent @event) {
        ArgumentNullException.ThrowIfNull(@event);

        switch (@event) {
            case AgentContentDelta content:
                _content.Append(content.Text);
                break;
            case AgentReasoningDelta reasoning:
                _reasoning.Append(reasoning.Text);
                break;
            case AgentToolCallDelta toolCall:
                if (!_toolCalls.TryGetValue(toolCall.Index, out ToolCallBuilder? builder)) {
                    builder = new ToolCallBuilder();
                    _toolCalls.Add(toolCall.Index, builder);
                }
                builder.Add(toolCall);
                break;
            case AgentUsageEvent usage:
                _usage = usage.Usage;
                break;
            case AgentFinished finished:
                _finishReason = finished.Reason;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@event));
        }
    }

    /// <summary>Returns the current complete response without changing the accumulator.</summary>
    public AgentChatResponse Complete() => new(
        _content.ToString(),
        _reasoning.ToString(),
        [.. _toolCalls.OrderBy(pair => pair.Key).Select(pair => pair.Value.Build(pair.Key))],
        _finishReason,
        _usage);

    private sealed class ToolCallBuilder {
        private readonly StringBuilder _id = new();
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();

        public void Add(AgentToolCallDelta delta) {
            if (delta.Id is not null) {
                _id.Append(delta.Id);
            }
            if (delta.Name is not null) {
                _name.Append(delta.Name);
            }
            if (delta.Arguments is not null) {
                _arguments.Append(delta.Arguments);
            }
        }

        public AgentToolCall Build(int index) => new(
            index,
            _id.Length == 0 ? null : _id.ToString(),
            _name.Length == 0 ? null : _name.ToString(),
            _arguments.ToString());
    }
}
