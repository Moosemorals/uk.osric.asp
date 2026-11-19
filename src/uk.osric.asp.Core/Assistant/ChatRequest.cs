// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>A function tool offered to the configured chat-completions provider.</summary>
/// <param name="Name">The stable tool name.</param>
/// <param name="Description">The provider-visible description.</param>
/// <param name="NeedsApproval">Whether RequireApproval mode pauses before execution.</param>
/// <param name="ParametersJson">The JSON Schema object for the tool arguments.</param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    bool NeedsApproval,
    string ParametersJson);

/// <summary>A provider request expressed only in framework-independent values.</summary>
/// <param name="Model">The configured model name.</param>
/// <param name="Messages">The ordered transcript for this step.</param>
/// <param name="Tools">The available functions.</param>
/// <param name="Temperature">The configured sampling temperature.</param>
/// <param name="MaxTokens">The provider's own <c>max_tokens</c>, computed for this request.</param>
/// <param name="ReasoningField">The provider field name used for optional reasoning deltas.</param>
public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    double Temperature,
    int MaxTokens,
    string ReasoningField);
