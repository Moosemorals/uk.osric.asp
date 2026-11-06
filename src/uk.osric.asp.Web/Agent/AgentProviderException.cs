// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

/// <summary>A provider failure that carries a message safe to show and a stable code for metrics.</summary>
internal sealed class AgentProviderException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException) {
    public string Code { get; } = code;
}
