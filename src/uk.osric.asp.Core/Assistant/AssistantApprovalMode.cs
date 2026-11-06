// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Controls whether tools marked for approval pause before execution.</summary>
public enum AssistantApprovalMode {
    /// <summary>Run flagged tools without an owner prompt.</summary>
    AlwaysApprove,

    /// <summary>Wait for an owner decision for each flagged tool call.</summary>
    RequireApproval,
}
