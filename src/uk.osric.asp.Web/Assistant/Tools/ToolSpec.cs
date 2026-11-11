// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;

/// <summary>One tool as it is offered to the model and validated when it is called.</summary>
/// <param name="Name">The tool's stable name.</param>
/// <param name="Description">The provider-visible description.</param>
/// <param name="NeedsApproval">Whether RequireApproval mode pauses before this tool runs.</param>
/// <param name="Arguments">The complete set of accepted arguments.</param>
internal sealed record ToolSpec(
    string Name,
    string Description,
    bool NeedsApproval,
    IReadOnlyList<ToolArgumentSchema> Arguments) {

    /// <summary>The one word the tool's refusals use, as in "Edit refused".</summary>
    public required string Verb { get; init; }

    /// <summary>Whether a successful call changes a stored page.</summary>
    public bool Writes { get; init; }

    /// <summary>The argument the path policy refuses before the owner is asked, or null.</summary>
    public string? PathArgument { get; init; }
}
