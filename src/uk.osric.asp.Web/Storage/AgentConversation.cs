// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

internal sealed class AgentConversation {
    public long Id { get; set; }
    public required string Owner { get; set; }
    public required string Title { get; set; }
    public bool TitleIsGenerated { get; set; } = true;
    public string ApprovalMode { get; set; } = "AlwaysApprove";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long NextMessageSequence { get; set; } = 1;
}
