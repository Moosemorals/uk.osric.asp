// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Provider token counts, when available.</summary>
/// <param name="PromptTokens">The input tokens the provider reported.</param>
/// <param name="CompletionTokens">The output tokens the provider reported.</param>
/// <param name="CachedTokens">The prompt tokens the provider served from its cache, when it says.</param>
/// <param name="ReasoningTokens">The completion tokens spent on reasoning, when the provider says.</param>
public sealed record TokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? CachedTokens = null,
    int? ReasoningTokens = null);
