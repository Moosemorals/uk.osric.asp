// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

/// <summary>Provider token counts, when available.</summary>
public sealed record AgentUsage(int? PromptTokens, int? CompletionTokens);
