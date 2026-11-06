// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text;

/// <summary>Combines provider deltas into one complete assistant response.</summary>
public sealed class ChatStreamAccumulator {
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly Dictionary<int, ToolCallBuilder> _toolCalls = [];
    private string? _finishReason;
    private TokenUsage? _usage;

    /// <summary>Adds one provider event to the current response.</summary>
    /// <param name="event">The provider event.</param>
    public void Add(ChatStreamEvent @event) {
        ArgumentNullException.ThrowIfNull(@event);

        switch (@event) {
            case ContentDelta content:
                _content.Append(content.Text);
                break;
            case ReasoningDelta reasoning:
                _reasoning.Append(reasoning.Text);
                break;
            case ToolCallDelta toolCall:
                if (!_toolCalls.TryGetValue(toolCall.Index, out ToolCallBuilder? builder)) {
                    builder = new ToolCallBuilder();
                    _toolCalls.Add(toolCall.Index, builder);
                }
                builder.Add(toolCall);
                break;
            case UsageEvent usage:
                _usage = usage.Usage;
                break;
            case Finished finished:
                _finishReason = finished.Reason;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@event));
        }
    }

    /// <summary>Returns the current complete response without changing the accumulator.</summary>
    public ChatResponse Complete() => new(
        _content.ToString(),
        _reasoning.ToString(),
        [.. _toolCalls.OrderBy(pair => pair.Key).Select(pair => pair.Value.Build(pair.Key))],
        _finishReason,
        _usage);

    private sealed class ToolCallBuilder {
        private readonly StringBuilder _id = new();
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();

        public void Add(ToolCallDelta delta) {
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

        public ToolCall Build(int index) => new(
            index,
            _id.Length == 0 ? null : _id.ToString(),
            _name.Length == 0 ? null : _name.ToString(),
            _arguments.ToString());
    }
}
