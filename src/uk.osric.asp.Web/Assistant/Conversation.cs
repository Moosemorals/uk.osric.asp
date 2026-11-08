// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Approval = Core.Assistant.ApprovalMode;

/// <summary>A stored conversation: its owner, title and approval mode.</summary>
internal sealed class Conversation {
    public long Id { get; set; }
    public required string Owner { get; set; }
    public required string Title { get; set; }
    public bool HasGeneratedTitle { get; set; } = true;
    public string ApprovalMode { get; set; } = Approval.AlwaysApprove.ToString();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long NextMessageSequence { get; set; } = 1;
}
