// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using uk.osric.asp.Core.Agent;

internal interface IChatCompletionClient {
    IAsyncEnumerable<AgentChatStreamEvent> StreamAsync(AgentChatRequest request, CancellationToken cancellationToken);
}

internal sealed class AgentProviderException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException) {
    public string Code { get; } = code;
}
