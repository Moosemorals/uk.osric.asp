// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.Options;

/// <summary>The assistant's configuration: provider, model, limits and the enabled switch.</summary>
internal sealed class AssistantOptions {
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
    public string ChatCompletionsPath { get; init; } = "/chat/completions";
    public string Model { get; init; } = "deepseek-flash";
    public string? ApiKey { get; init; }
    public string ReasoningField { get; init; } = "reasoning_content";
    public bool RequireApiKey { get; init; } = true;
    public bool AllowInsecureHttp { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 32_768;
    public bool IncludeUsage { get; init; } = true;
    public int MaxSteps { get; init; } = 120;
    public int MaxToolCalls { get; init; } = 32;
    public int MaxToolCallsPerStep { get; init; } = 8;
    public int MaxActiveRuns { get; init; } = 2;
    public int MaxQueuedRuns { get; init; } = 8;
    public double RequestTimeoutSeconds { get; init; } = 300;
    public double RunTimeoutSeconds { get; init; } = 300;
    public double ToolTimeoutSeconds { get; init; } = 30;
    public int MaxProviderRetries { get; init; } = 1;
    public int MaxMessageChars { get; init; } = 16_384;
    public int MaxPageChars { get; init; } = 262_144;
    public int MaxToolArgumentChars { get; init; } = 131_072;
    public int MaxToolResultChars { get; init; } = 32_768;
    /// <summary>The estimated prompt tokens one request may spend.</summary>
    public int MaxContextTokens { get; init; } = 90_000;
    /// <summary>The model's whole context window, used to check the prompt and output budgets fit.</summary>
    public int ModelContextTokens { get; init; } = 131_072;
    /// <summary>The most memories one owner may hold; past this <c>remember</c> refuses.</summary>
    public int MaxMemoriesPerOwner { get; init; } = 200;
    /// <summary>The longest memory, in characters. One longer is refused, never truncated.</summary>
    public int MaxMemoryChars { get; init; } = 1_000;
    /// <summary>The most memories one run may be given.</summary>
    public int MaxMemoriesPerRun { get; init; } = 12;
    /// <summary>The most characters a run's memory block may cost.</summary>
    public int MaxMemoryCharsPerRun { get; init; } = 4_000;
    /// <summary>The most candidates a memory search ranks, for recall and for the tool.</summary>
    public int MaxMemorySearchResults { get; init; } = 50;
    /// <summary>The most terms one message contributes to a recall query.</summary>
    public int MaxMemoryQueryTerms { get; init; } = 12;
    public int HeartbeatSeconds { get; init; } = 15;
    public int JournalMaxEvents { get; init; } = 20_000;
    public int JournalMaxBytes { get; init; } = 4_194_304;
    public int JournalTtlMinutes { get; init; } = 10;
    /// <summary>The site's routes are already read-only to the assistant, so this list is the
    /// operator's extra lock over a page it may not touch. Empty by default: the owner can still edit
    /// and restore a locked page in the editor.</summary>
    public string[] ProtectedPaths { get; init; } = [];
    public string WritablePathPrefix { get; init; } = "";
}

/// <summary>Rejects an assistant configuration its client could not use.</summary>
internal sealed class AssistantOptionsValidator : IValidateOptions<AssistantOptions> {
    private const int MinimumContextTokens = 16_384;

    public ValidateOptionsResult Validate(string? name, AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled) {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (options.RequireApiKey && string.IsNullOrWhiteSpace(options.ApiKey)) {
            failures.Add("Assistant is enabled, but Assistant__ApiKey is required.");
        }
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && !(baseUri.IsLoopback || options.AllowInsecureHttp))) {
            failures.Add("Assistant:BaseUrl must be an absolute HTTPS URL unless it is loopback or Assistant:AllowInsecureHttp is true.");
        } else if (baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0) {
            failures.Add("Assistant:BaseUrl must not contain credentials, a query or a fragment.");
        }
        if (string.IsNullOrWhiteSpace(options.Model)) {
            failures.Add("Assistant:Model must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(options.ChatCompletionsPath)
            || !options.ChatCompletionsPath.StartsWith('/')) {
            failures.Add("Assistant:ChatCompletionsPath must be an absolute path.");
        }
        if (options.Temperature is < 0 or > 2 || double.IsNaN(options.Temperature)) {
            failures.Add("Assistant:Temperature must be between 0 and 2.");
        }
        if (options.MaxOutputTokens <= 0 || options.MaxSteps <= 0 || options.MaxToolCalls <= 0
            || options.MaxToolCallsPerStep <= 0 || options.MaxActiveRuns <= 0 || options.MaxQueuedRuns <= 0
            || !double.IsFinite(options.RequestTimeoutSeconds) || !double.IsFinite(options.RunTimeoutSeconds)
            || !double.IsFinite(options.ToolTimeoutSeconds)
            || options.RequestTimeoutSeconds <= 0 || options.RunTimeoutSeconds <= 0 || options.ToolTimeoutSeconds <= 0
            || options.MaxProviderRetries < 0 || options.MaxMessageChars <= 0 || options.MaxPageChars <= 0
            || options.MaxToolArgumentChars <= 0 || options.MaxToolResultChars <= 0
            || options.MaxContextTokens <= 0 || options.ModelContextTokens <= 0
            || options.HeartbeatSeconds <= 0 || options.JournalMaxEvents <= 0
            || options.JournalMaxBytes <= 0 || options.JournalTtlMinutes <= 0) {
            failures.Add("Assistant limits and timeouts must be positive; MaxProviderRetries may be zero.");
        }
        if (options.MaxContextTokens < MinimumContextTokens) {
            failures.Add($"Assistant:MaxContextTokens must be at least {MinimumContextTokens}.");
        }
        if ((long)options.MaxContextTokens + options.MaxOutputTokens > options.ModelContextTokens) {
            failures.Add(
                $"Assistant:MaxContextTokens plus Assistant:MaxOutputTokens must not exceed Assistant:ModelContextTokens "
                + $"({options.MaxContextTokens} + {options.MaxOutputTokens} > {options.ModelContextTokens}).");
        }
        if (options.MaxMemoriesPerOwner <= 0 || options.MaxMemoryChars <= 0
            || options.MaxMemoriesPerRun <= 0 || options.MaxMemoryCharsPerRun <= 0
            || options.MaxMemorySearchResults <= 0) {
            failures.Add("Assistant memory limits must be positive.");
        }
        if (options.MaxMemoryChars > options.MaxMemoryCharsPerRun) {
            failures.Add("Assistant:MaxMemoryChars must not exceed Assistant:MaxMemoryCharsPerRun.");
        }
        if (options.MaxMemoryQueryTerms < 2) {
            failures.Add("Assistant:MaxMemoryQueryTerms must be at least 2, so a query is not a single letter.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
