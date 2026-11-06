// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>One row of the conversation list, with the run still in progress.</summary>
internal sealed record ConversationListItem(
    long Id,
    string Title,
    string ApprovalMode,
    DateTimeOffset UpdatedAt,
    string? ActiveRunId);
