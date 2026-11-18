// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Each message's serialized size, measured once, and the arithmetic over those figures.</summary>
/// <remarks>
/// Masking one payload and dropping one turn are both changes to a known part of the body, so the
/// planner and the trimmer add and subtract sizes instead of serializing every candidate they build.
/// The body is the fixed part — model, tools, sampling fields and the array brackets — plus the
/// messages separated by commas, so the fixed part is whatever the exact measure leaves over.
/// </remarks>
internal sealed class PromptSizes {

    private readonly long _overhead;
    private readonly long[] _sizes;
    private readonly Dictionary<ChatMessage, long> _sizeOf;

    private PromptSizes(long overhead, long[] sizes, Dictionary<ChatMessage, long> sizeOf) {
        _overhead = overhead;
        _sizes = sizes;
        _sizeOf = sizeOf;
    }

    /// <summary>Each message's serialized size, in the request's own order.</summary>
    public IReadOnlyList<long> Sizes => _sizes;

    /// <summary>The exact bytes of the request as it was measured.</summary>
    public long Total => Body(Sum(_sizes), _sizes.Length);

    /// <summary>Measures each message of a request once.</summary>
    /// <param name="request">The request to measure.</param>
    /// <returns>The sizes, and the fixed part of the body they are added to.</returns>
    public static PromptSizes Measure(ChatRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        long[] sizes = new long[request.Messages.Count];
        Dictionary<ChatMessage, long> sizeOf = [with(request.Messages.Count)];
        for (int index = 0; index < sizes.Length; index++) {
            ChatMessage message = request.Messages[index];
            sizes[index] = ChatRequestJson.MeasureMessage(message);
            sizeOf[message] = sizes[index];
        }

        long total = ChatRequestJson.MeasureBytes(request);
        long overhead = total - Sum(sizes) - Math.Max(0, sizes.Length - 1);
        return new PromptSizes(overhead, sizes, sizeOf);
    }

    /// <summary>The body the same request would cost with this message sum and count.</summary>
    /// <param name="messageBytes">The summed sizes of the messages kept.</param>
    /// <param name="count">How many messages are kept.</param>
    /// <returns>The exact byte count the body would have.</returns>
    public long Body(long messageBytes, int count) => _overhead + messageBytes + Math.Max(0, count - 1);

    /// <summary>The size of a message this instance measured.</summary>
    /// <param name="message">A message from the measured request.</param>
    /// <returns>The message's serialized size.</returns>
    public long SizeOf(ChatMessage message) {
        ArgumentNullException.ThrowIfNull(message);
        return _sizeOf.TryGetValue(message, out long size)
            ? size
            : ChatRequestJson.MeasureMessage(message);
    }

    /// <summary>The body the same request would cost carrying exactly these messages.</summary>
    /// <param name="messages">The messages to carry.</param>
    /// <returns>The exact byte count the body would have.</returns>
    public long Of(IReadOnlyList<ChatMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        long sum = 0;
        foreach (ChatMessage message in messages) {
            sum += SizeOf(message);
        }
        return Body(sum, messages.Count);
    }

    private static long Sum(IReadOnlyList<long> sizes) {
        long sum = 0;
        foreach (long size in sizes) {
            sum += size;
        }
        return sum;
    }
}
