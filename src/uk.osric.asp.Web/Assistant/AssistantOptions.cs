// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.Options;
using uk.osric.asp.Web.Configuration;

/// <summary>The assistant's configuration: provider, model and limits.</summary>
/// <remarks>
/// Every value comes from <c>Assistant</c> and none has a fallback: a missing or unusable one stops
/// the site at startup. <c>ApiKey</c> is required unless the provider does not need one, which
/// <see cref="RequireApiKey"/> says.
/// </remarks>
internal sealed record AssistantOptions {
    public required string BaseUrl { get; init; }
    public required string ChatCompletionsPath { get; init; }
    public required string Model { get; init; }
    public required string? ApiKey { get; init; }
    public required string ReasoningField { get; init; }
    public required bool RequireApiKey { get; init; }
    public required bool AllowInsecureHttp { get; init; }
    public required double Temperature { get; init; }

    /// <summary>The path the provider publishes its model list on, such as <c>/models</c>.</summary>
    public required string ModelsPath { get; init; }

    /// <summary>How long the model list read waits for the provider before it gives up.</summary>
    public required double ModelsTimeoutSeconds { get; init; }

    /// <summary>
    /// The floor under a reply, in tokens. The window the provider reports is shared between the
    /// prompt and the reply, so what the prompt leaves is the reply's allowance, capped at the
    /// provider's own maximum and never below this.
    /// </summary>
    public required int MinOutputTokens { get; init; }

    public required int MaxSteps { get; init; }
    public required int MaxToolCalls { get; init; }
    public required int MaxToolCallsPerStep { get; init; }
    public required int MaxActiveRuns { get; init; }
    public required int MaxQueuedRuns { get; init; }
    public required double RequestTimeoutSeconds { get; init; }
    public required double RunTimeoutSeconds { get; init; }
    public required double ToolTimeoutSeconds { get; init; }

    /// <summary>How long the account balance panel waits for the provider before it shows nothing.</summary>
    public required double BalanceTimeoutSeconds { get; init; }

    /// <summary>How often the balance sampler reads the provider account's credit.</summary>
    public required int BalanceSampleSeconds { get; init; }

    public required int MaxProviderRetries { get; init; }

    /// <summary>The most characters a stored page may carry: the page store's bound, not a context rule.</summary>
    public required int MaxPageChars { get; init; }

    /// <summary>
    /// The most characters one tool call's arguments may carry. It must exceed <see cref="MaxPageChars"/>
    /// with room for the envelope, because a page the store accepts must be writable through a tool.
    /// </summary>
    public required int MaxToolArgumentChars { get; init; }

    /// <summary>The most memories one owner may hold; past this <c>remember</c> refuses.</summary>
    public required int MaxMemoriesPerOwner { get; init; }

    /// <summary>The longest memory, in characters. One longer is refused, never truncated.</summary>
    public required int MaxMemoryChars { get; init; }

    /// <summary>The most candidates a memory search ranks, for recall and for the tool.</summary>
    public required int MaxMemorySearchResults { get; init; }

    /// <summary>The most terms one message contributes to a recall query.</summary>
    public required int MaxMemoryQueryTerms { get; init; }

    public required int HeartbeatSeconds { get; init; }
    public required int JournalMaxEvents { get; init; }
    public required int JournalMaxBytes { get; init; }
    public required int JournalTtlMinutes { get; init; }

    /// <summary>A page-key prefix the assistant may write within; blank means the whole site.</summary>
    public required string WritablePathPrefix { get; init; }

    /// <summary>What one <c>search_pages</c> call may return.</summary>
    public required int MaxSearchMatches { get; init; }

    /// <summary>Rows a listing answers with when the caller names no <c>limit</c>.</summary>
    public required int DefaultListLimit { get; init; }

    /// <summary>The most rows one listing will answer with.</summary>
    public required int MaxListLimit { get; init; }

    /// <summary>Lines one virtual-shell <c>grep</c> may show before it says there are more.</summary>
    public required int ShellMatchLimit { get; init; }

    /// <summary>Lines <c>head</c> and <c>tail</c> show when no count is given.</summary>
    public required int ShellDefaultLines { get; init; }

    /// <summary>Reads <c>Assistant</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static AssistantOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Assistant");
        AssistantOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Assistant</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Assistant</c>, which the caller completes.</param>
    internal static AssistantOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        bool requireApiKey = reader.Switch("RequireApiKey", recommended: true);
        string? apiKey = reader.OptionalText("ApiKey");

        AssistantOptions options = new() {
            BaseUrl = reader.Text("BaseUrl", "an absolute URL such as https://api.deepseek.com"),
            ChatCompletionsPath = reader.Text("ChatCompletionsPath", "a path such as /chat/completions"),
            Model = reader.Text("Model", "the provider's model name, such as deepseek-flash"),
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            ReasoningField = reader.Text("ReasoningField", "the JSON field a reasoning model streams, such as reasoning_content"),
            RequireApiKey = requireApiKey,
            AllowInsecureHttp = reader.Switch("AllowInsecureHttp", recommended: false),
            Temperature = reader.Decimal("Temperature", recommended: 0.2, minimum: 0, maximum: 2),
            ModelsPath = reader.Text("ModelsPath", "a path such as /models"),
            ModelsTimeoutSeconds = reader.Decimal("ModelsTimeoutSeconds", recommended: 3, minimum: 1, maximum: 60),
            MinOutputTokens = reader.Number("MinOutputTokens", recommended: 131_072, minimum: 1_024, maximum: 2_000_000),
            MaxSteps = reader.Number("MaxSteps", recommended: 160, minimum: 1, maximum: 1_000),
            MaxToolCalls = reader.Number("MaxToolCalls", recommended: 128, minimum: 1, maximum: 1_000),
            MaxToolCallsPerStep = reader.Number("MaxToolCallsPerStep", recommended: 64, minimum: 1, maximum: 1_000),
            MaxActiveRuns = reader.Number("MaxActiveRuns", recommended: 2, minimum: 1, maximum: 16),
            MaxQueuedRuns = reader.Number("MaxQueuedRuns", recommended: 8, minimum: 1, maximum: 256),
            RequestTimeoutSeconds = reader.Decimal("RequestTimeoutSeconds", recommended: 120, minimum: 1, maximum: 3_600),
            RunTimeoutSeconds = reader.Decimal("RunTimeoutSeconds", recommended: 120, minimum: 1, maximum: 86_400),
            ToolTimeoutSeconds = reader.Decimal("ToolTimeoutSeconds", recommended: 10, minimum: 1, maximum: 3_600),
            BalanceTimeoutSeconds = reader.Decimal("BalanceTimeoutSeconds", recommended: 3, minimum: 1, maximum: 60),
            BalanceSampleSeconds = reader.Number("BalanceSampleSeconds", recommended: 60, minimum: 30, maximum: 3_600),
            MaxProviderRetries = reader.Number("MaxProviderRetries", recommended: 1, minimum: 0, maximum: 10),
            MaxPageChars = reader.Number("MaxPageChars", recommended: 262_144, minimum: 1, maximum: 4_194_304),
            MaxToolArgumentChars = reader.Number("MaxToolArgumentChars", recommended: 270_336, minimum: 1, maximum: 4_194_304),
            MaxMemoriesPerOwner = reader.Number("MaxMemoriesPerOwner", recommended: 200, minimum: 1, maximum: 10_000),
            MaxMemoryChars = reader.Number("MaxMemoryChars", recommended: 1_000, minimum: 1, maximum: 100_000),
            MaxMemorySearchResults = reader.Number("MaxMemorySearchResults", recommended: 50, minimum: 1, maximum: 1_000),
            MaxMemoryQueryTerms = reader.Number("MaxMemoryQueryTerms", recommended: 12, minimum: 2, maximum: 100),
            HeartbeatSeconds = reader.Number("HeartbeatSeconds", recommended: 15, minimum: 1, maximum: 300),
            JournalMaxEvents = reader.Number("JournalMaxEvents", recommended: 20_000, minimum: 1, maximum: 1_000_000),
            JournalMaxBytes = reader.Number("JournalMaxBytes", recommended: 4_194_304, minimum: 1_024, maximum: 1_073_741_824),
            JournalTtlMinutes = reader.Number("JournalTtlMinutes", recommended: 10, minimum: 1, maximum: 1_440),
            WritablePathPrefix = reader.Text("WritablePathPrefix", "a page-key prefix such as /drafts, or a blank string for the whole site",
                allowEmpty: true),
            MaxSearchMatches = reader.Number("MaxSearchMatches", recommended: 1_000, minimum: 1, maximum: 1_000),
            DefaultListLimit = reader.Number("DefaultListLimit", recommended: 50, minimum: 1, maximum: 1_000),
            MaxListLimit = reader.Number("MaxListLimit", recommended: 500, minimum: 1, maximum: 1_000),
            ShellMatchLimit = reader.Number("ShellMatchLimit", recommended: 100, minimum: 1, maximum: 1_000),
            ShellDefaultLines = reader.Number("ShellDefaultLines", recommended: 10, minimum: 1, maximum: 1_000),
        };

        reader.FailIf(requireApiKey && options.ApiKey is null, "ApiKey",
            "is not set. Set it to the provider's key, or set Assistant:RequireApiKey to false for a provider that needs none");
        reader.FailIf(options.DefaultListLimit > options.MaxListLimit, "DefaultListLimit",
            $"is {options.DefaultListLimit}, which is larger than MaxListLimit ({options.MaxListLimit})");

        ValidateOptionsResult validation = new AssistantOptionsValidator().Validate(name: null, options);
        if (validation.Failed) {
            foreach (string failure in validation.Failures) {
                reader.Fail(failure);
            }
        }

        return options;
    }
}
