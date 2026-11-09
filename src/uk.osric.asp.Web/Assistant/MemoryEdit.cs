// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>A version a memory was replaced from, kept so an update leaves a trail.</summary>
/// <remarks>
/// The key is the memory and the version that was replaced, and the foreign key to the head is real:
/// history has no meaning once its memory is forgotten, so a forget takes the trail with it.
/// </remarks>
internal sealed class MemoryEdit {

    /// <summary>The memory this version belonged to.</summary>
    public long MemoryId { get; set; }

    /// <summary>The version number the replaced content carried.</summary>
    public int Version { get; set; }

    /// <summary>The kind as it was.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The content as it was.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>When it was replaced.</summary>
    public DateTimeOffset EditedAt { get; set; }

    /// <summary>The conversation the edit was made in, or null when the assistant made it in a run
    /// whose conversation has since been deleted.</summary>
    public long? SourceConversationId { get; set; }
}
