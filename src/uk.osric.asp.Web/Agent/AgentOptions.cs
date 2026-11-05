// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using Microsoft.Extensions.Options;

internal sealed class AgentOptions {
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "deepseek";
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
    public string ChatCompletionsPath { get; init; } = "/chat/completions";
    public string Model { get; init; } = "deepseek-flash";
    public string? ApiKey { get; init; }
    public string ReasoningField { get; init; } = "reasoning_content";
    public bool RequireApiKey { get; init; } = true;
    public bool AllowInsecureHttp { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 4096;
    public bool IncludeUsage { get; init; } = true;
    public int MaxSteps { get; init; } = 12;
    public int MaxToolCalls { get; init; } = 32;
    public int MaxToolCallsPerStep { get; init; } = 8;
    public int MaxActiveRuns { get; init; } = 2;
    public int MaxQueuedRuns { get; init; } = 8;
    public int RequestTimeoutSeconds { get; init; } = 300;
    public int RunTimeoutSeconds { get; init; } = 600;
    public int ToolTimeoutSeconds { get; init; } = 30;
    public int MaxProviderRetries { get; init; } = 1;
    public int MaxMessageChars { get; init; } = 16_384;
    public int MaxPageChars { get; init; } = 262_144;
    public int MaxToolArgumentChars { get; init; } = 131_072;
    public int MaxToolResultChars { get; init; } = 32_768;
    public int MaxContextChars { get; init; } = 120_000;
    public int HeartbeatSeconds { get; init; } = 15;
    public int JournalMaxEvents { get; init; } = 20_000;
    public int JournalMaxBytes { get; init; } = 4_194_304;
    public int JournalTtlMinutes { get; init; } = 10;
    public string[] ProtectedPaths { get; init; } = [
        "/assistant.js", "/assistant.css", "/editor.js", "/editor.css",
        "/layout.css", "/site.css", "/status.css", "/rocket.svg",
    ];
    public string WritablePathPrefix { get; init; } = "";
}

internal sealed class AgentOptionsValidator : IValidateOptions<AgentOptions> {
    private const int MinimumContextChars = 16_384;

    public ValidateOptionsResult Validate(string? name, AgentOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled) {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (options.RequireApiKey && string.IsNullOrWhiteSpace(options.ApiKey)) {
            failures.Add("Agent is enabled, but Agent__ApiKey is required.");
        }
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && !(baseUri.IsLoopback || options.AllowInsecureHttp))) {
            failures.Add("Agent:BaseUrl must be an absolute HTTPS URL unless it is loopback or Agent:AllowInsecureHttp is true.");
        } else if (baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0) {
            failures.Add("Agent:BaseUrl must not contain credentials, a query or a fragment.");
        }
        if (string.IsNullOrWhiteSpace(options.Model)) {
            failures.Add("Agent:Model must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(options.ChatCompletionsPath)
            || !options.ChatCompletionsPath.StartsWith("/", StringComparison.Ordinal)) {
            failures.Add("Agent:ChatCompletionsPath must be an absolute path.");
        }
        if (options.Temperature is < 0 or > 2 || double.IsNaN(options.Temperature)) {
            failures.Add("Agent:Temperature must be between 0 and 2.");
        }
        if (options.MaxOutputTokens <= 0 || options.MaxSteps <= 0 || options.MaxToolCalls <= 0
            || options.MaxToolCallsPerStep <= 0 || options.MaxActiveRuns <= 0 || options.MaxQueuedRuns <= 0
            || options.RequestTimeoutSeconds <= 0 || options.RunTimeoutSeconds <= 0 || options.ToolTimeoutSeconds <= 0
            || options.MaxProviderRetries < 0 || options.MaxMessageChars <= 0 || options.MaxPageChars <= 0
            || options.MaxToolArgumentChars <= 0 || options.MaxToolResultChars <= 0
            || options.HeartbeatSeconds <= 0 || options.JournalMaxEvents <= 0
            || options.JournalMaxBytes <= 0 || options.JournalTtlMinutes <= 0) {
            failures.Add("Agent limits and timeouts must be positive; MaxProviderRetries may be zero.");
        }
        if (options.MaxContextChars < MinimumContextChars) {
            failures.Add($"Agent:MaxContextChars must be at least {MinimumContextChars}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
