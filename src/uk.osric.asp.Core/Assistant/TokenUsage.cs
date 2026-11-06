// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Provider token counts, when available.</summary>
public sealed record TokenUsage(int? PromptTokens, int? CompletionTokens);
