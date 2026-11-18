// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>One time a run stopped and asked its owner for something.</summary>
/// <remarks>
/// The run waits on one thing at a time, so the run's id is the key: a second interrupt for a run that
/// already has one is a defect in the caller, not a second question. The request and the resolution are
/// the host's own JSON and the store never reads inside them, which is what lets a new kind of question
/// be added without a column, a table or a migration.
/// </remarks>
internal sealed class OwnerInterrupt {
    public required string RunId { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }

    /// <summary>The call the wait belongs to, or null when the wait is not about a tool call.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>What the owner is shown, as <see cref="InterruptPayloads"/> writes it.</summary>
    public required string RequestJson { get; set; }

    /// <summary>What the owner chose or typed, once they have.</summary>
    public string? ResolutionJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? FailureCode { get; set; }
}

/// <summary>The kinds of thing a run can stop to ask its owner.</summary>
internal static class OwnerInterruptKind {

    /// <summary>A flagged tool call waits for the owner to allow or refuse it before it runs.</summary>
    public const string Approval = "approval";

    /// <summary>A run that spent a budget waits for the owner to tell it to carry on.</summary>
    public const string Continuation = "continuation";
}

/// <summary>The states an interrupt can be in.</summary>
/// <remarks>
/// One status per state rather than one per resolution: what the owner decided lives in the
/// resolution, so every kind shares the same three words and a new kind does not invent a vocabulary.
/// </remarks>
internal static class OwnerInterruptStatus {
    public const string Pending = "pending";
    public const string Resolved = "resolved";
    public const string Failed = "failed";
}
