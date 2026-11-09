// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>What a memory write did: stored a memory, or refused with something the model can act on.</summary>
/// <param name="IsSuccess">Whether the store changed.</param>
/// <param name="Memory">The memory as it now stands, when the write succeeded.</param>
/// <param name="Error">A sentence the model can act on, when it did not.</param>
internal sealed record MemoryWriteResult(bool IsSuccess, MemoryCandidate? Memory, string? Error) {

    /// <summary>A write that left the store holding <paramref name="memory"/>.</summary>
    /// <param name="memory">The memory as it now stands, which for a duplicate is the row already
    /// there rather than a new one.</param>
    public static MemoryWriteResult Stored(MemoryCandidate memory) => new(true, memory, null);

    /// <summary>A write that changed nothing, and why.</summary>
    /// <param name="error">The sentence the tool result carries.</param>
    public static MemoryWriteResult Refused(string error) => new(false, null, error);
}
