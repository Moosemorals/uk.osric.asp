// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Configuration;

/// <summary>Reads the provider's model list and keeps the budget the answer supports.</summary>
/// <remarks>
/// The read happens once at startup and again when a request is rejected for length, because that
/// rejection is the one signal that the cached window is stale. It is never fatal on its own: the
/// assistant is one feature, and a provider that is down must not take the page server with it. The
/// one answer that is fatal is the provider saying the configured model does not exist, because that
/// is authoritative and the owner should hear it at deploy time rather than from a failed run.
/// </remarks>
internal sealed class ModelWindowSampler(
    IServiceScopeFactory scopes,
    ModelWindowSource source,
    AssistantOptions options,
    ILogger<ModelWindowSampler> logger) {

    private ModelWindowReason? _lastReason;
    private ModelWindowRead? _lastAnswer;

    /// <summary>The budget the last read supports, or null when there is no window.</summary>
    public ContextBudget? Budget => source.Budget;

    /// <summary>The last read.</summary>
    public ModelWindowRead Read => source.Read;

    /// <summary>Reads the model list and records what it supports.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>What the provider answered.</returns>
    /// <exception cref="ConfigurationException">The provider says the configured model does not exist.</exception>
    public async Task<ModelWindowRead> RefreshAsync(CancellationToken cancellationToken) {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IProviderModelsClient client = scope.ServiceProvider.GetRequiredService<IProviderModelsClient>();
        ModelWindowRead read = await client.GetAsync(cancellationToken);

        LogChange(read);
        if (read.Reason == ModelWindowReason.ModelMissing) {
            throw new ConfigurationException(
                $"Assistant:Model is '{options.Model}', which the provider's model list does not contain. "
                + "Set it to a model the provider offers, or leave it alone until the provider publishes it.");
        }

        // A floor that leaves too little of the window for the prompt is the one combination of a
        // configured value and a reported one that cannot be true, so it is refused where the other
        // config failures are refused.
        try {
            source.Record(read, options.MinOutputTokens);
        } catch (ArgumentOutOfRangeException) {
            throw new ConfigurationException(
                $"Assistant:MinOutputTokens is {options.MinOutputTokens}, which leaves too little of the "
                + $"provider's {read.ContextWindow} token window for the prompt. Lower it; the floor must "
                + $"leave at least {ContextBudget.MinimumPromptTokens} tokens for the prompt.");
        }

        return read;
    }

    private void LogChange(ModelWindowRead read) {
        bool recovered = _lastReason is not null && read.Reason == ModelWindowReason.Ok;
        _lastReason = read.Reason;
        switch (read.Reason) {
            case ModelWindowReason.Ok when _lastAnswer?.SameAs(read) == true:
                break;
            case ModelWindowReason.Ok when recovered:
                logger.LogInformation("The provider is answering the model list again, with a {ContextWindow} token window",
                    read.ContextWindow);
                break;
            case ModelWindowReason.Ok:
                logger.LogInformation(
                    "The provider reports a {ContextWindow} token window and a {MaximumOutputTokens} token output limit for {Model}",
                    read.ContextWindow, read.MaximumOutputTokens, options.Model);
                break;
            case ModelWindowReason.ModelMissing:
                logger.LogError("The provider's model list does not contain {Model}", options.Model);
                break;
            case ModelWindowReason.Incomplete:
                logger.LogWarning(
                    "The provider's entry for {Model} omits its context window or its output limit, so the assistant cannot run",
                    options.Model);
                break;
            case ModelWindowReason.Timeout:
                logger.LogWarning("The provider did not answer the model list request in time");
                break;
            case ModelWindowReason.Unreachable:
                logger.LogWarning("The provider could not be reached for the model list");
                break;
            case ModelWindowReason.Invalid:
                logger.LogWarning("The provider returned an invalid model list");
                break;
            case ModelWindowReason.Error:
            case ModelWindowReason.Unknown:
            default:
                logger.LogWarning("The provider answered the model list request with an error");
                break;
        }

        _lastAnswer = read;
    }
}
