// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>One memory: the head row, which is the current version and the only one recall reads.</summary>
/// <remarks>
/// The owner is the security boundary: every statement filters on it. Provenance is deliberately not a
/// foreign key, because a memory outlives the conversation that produced it (DEC-184).
/// </remarks>
internal sealed class MemoryEntry {

    /// <summary>The row's id, which the model names in <c>update_memory</c> and <c>forget_memory</c>.</summary>
    public long Id { get; set; }

    /// <summary>The account the memory belongs to.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>The kind's stored name, one of <c>preference</c>, <c>fact</c>, <c>feedback</c> or
    /// <c>reference</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The normalised note.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The version an update has to carry to be accepted.</summary>
    public int Version { get; set; }

    /// <summary>The owner's "always use this". The assistant cannot set it.</summary>
    public bool Pinned { get; set; }

    /// <summary>The conversation that wrote this version, or null once that conversation is deleted.</summary>
    public long? SourceConversationId { get; set; }

    /// <summary>The run that wrote this version, or null once that conversation is deleted.</summary>
    public string? SourceRunId { get; set; }

    /// <summary>When the memory was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this version was written.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When a run last recalled it.</summary>
    public DateTimeOffset? LastRecalledAt { get; set; }

    /// <summary>The conversation that last recalled it, which is the stickiness recall uses.</summary>
    public long? LastRecalledConversationId { get; set; }

    /// <summary>How many runs have recalled it.</summary>
    public int RecallCount { get; set; }
}
