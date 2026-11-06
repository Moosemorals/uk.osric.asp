// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using uk.osric.asp.Core.Agent;

/// <summary>Streams a completion from the configured provider.</summary>
internal interface IChatCompletionClient {
    IAsyncEnumerable<AgentChatStreamEvent> StreamAsync(AgentChatRequest request, CancellationToken cancellationToken);
}
