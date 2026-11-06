// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Streams a completion from the configured provider.</summary>
internal interface IChatCompletionClient {
    IAsyncEnumerable<AssistantChatStreamEvent> StreamAsync(AssistantChatRequest request, CancellationToken cancellationToken);
}
