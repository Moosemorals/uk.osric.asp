// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.Options;

/// <summary>Rejects an assistant configuration whose values cannot all be true at once.</summary>
/// <remarks>
/// The reader owns "present, a number, inside its range" for every key; this owns the rules a range
/// cannot express. A disabled assistant is not asked about its provider, because nothing reaches one.
/// </remarks>
internal sealed class AssistantOptionsValidator : IValidateOptions<AssistantOptions> {

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled) {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && !(baseUri.IsLoopback || options.AllowInsecureHttp))) {
            failures.Add("Assistant:BaseUrl must be an absolute HTTPS URL unless it is loopback or Assistant:AllowInsecureHttp is true.");
        } else if (baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0) {
            failures.Add("Assistant:BaseUrl must not contain credentials, a query or a fragment.");
        }
        if (!options.ChatCompletionsPath.StartsWith('/')) {
            failures.Add("Assistant:ChatCompletionsPath must be an absolute path.");
        }
        if ((long)options.MaxContextTokens + options.MaxOutputTokens > options.ModelContextTokens) {
            failures.Add(
                $"Assistant:MaxContextTokens plus Assistant:MaxOutputTokens must not exceed Assistant:ModelContextTokens "
                + $"({options.MaxContextTokens} + {options.MaxOutputTokens} > {options.ModelContextTokens}).");
        }
        if (options.MaxMemoryChars > options.MaxMemoryCharsPerRun) {
            failures.Add("Assistant:MaxMemoryChars must not exceed Assistant:MaxMemoryCharsPerRun.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
