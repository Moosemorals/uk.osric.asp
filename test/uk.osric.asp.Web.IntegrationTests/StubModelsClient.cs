// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Web.Assistant;

/// <summary>Answers the model list the way the provider's own documentation does.</summary>
/// <remarks>
/// No fixture may reach a socket for the model's window, so every host reads this instead. The
/// figures are the provider's documented example — deepseek-flash, 1,048,576 tokens of window and
/// 393,216 of output — unless a fixture is about a window that makes a conversation too big.
/// </remarks>
internal sealed class StubModelsClient : IProviderModelsClient {

    /// <summary>The window and output limit the provider's own example reports.</summary>
    public const long DefaultWindow = 1_048_576;

    /// <summary>The output limit the provider's own example reports.</summary>
    public const int DefaultOutput = 393_216;

    /// <summary>The window to answer with.</summary>
    public long ContextWindow { get; init; } = DefaultWindow;

    /// <summary>The output limit to answer with.</summary>
    public int MaximumOutputTokens { get; init; } = DefaultOutput;

    /// <summary>What to answer with instead, when a fixture is about a provider that said nothing.</summary>
    public ModelWindowRead? Answer { get; init; }

    /// <inheritdoc/>
    public Task<ModelWindowRead> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer ?? ModelWindowRead.Ok(ContextWindow, MaximumOutputTokens));
}
