// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.RegularExpressions;

/// <summary>Refuses a memory that looks like a secret, best-effort, with the rule that refused it.</summary>
/// <remarks>
/// This is a guard, not a sanitiser: it exists because the model is told never to store a secret and a
/// prompt is not a guarantee. It has to let a sentence <em>about</em> a secret through — "the API key
/// lives in the deployment environment" is exactly the kind of note worth keeping — so it looks for the
/// shapes a secret is written in rather than the words around it.
/// </remarks>
public static class MemorySecrets {

    /// <summary>The rule names the refusal quotes.</summary>
    public const string PrivateKey = "a private key";

    /// <summary>The rule name for an assignment to a credential-shaped name.</summary>
    public const string Assignment = "a password or key assignment";

    /// <summary>The rule name for a provider-shaped key.</summary>
    public const string ProviderKey = "a provider key";

    /// <summary>The rule name for a long base64-like run.</summary>
    public const string EncodedValue = "a long encoded value";

    private static readonly Regex s_privateKey = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex s_assignment = new(
        @"\b(?:pass(?:word|wd|phrase)?|secret|token|api[_-]?key|access[_-]?key|client[_-]?secret)\b\s*[:=]\s*\S{4,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex s_providerKey = new(
        @"\bsk-[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex s_encoded = new(
        @"\b(?=[A-Za-z0-9+/]*[a-z])(?=[A-Za-z0-9+/]*[A-Z])(?=[A-Za-z0-9+/]*\d)[A-Za-z0-9+/]{40,}={0,2}(?![A-Za-z0-9+/=])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The rule a memory's text looks like a secret by, or null when it looks like prose.</summary>
    /// <param name="content">The normalised text of a memory.</param>
    /// <returns>The name of the first rule that matched, or null.</returns>
    public static string? LooksLikeSecret(string content) {
        ArgumentNullException.ThrowIfNull(content);

        if (s_privateKey.IsMatch(content)) {
            return PrivateKey;
        }
        if (s_assignment.IsMatch(content)) {
            return Assignment;
        }
        if (s_providerKey.IsMatch(content)) {
            return ProviderKey;
        }
        return s_encoded.IsMatch(content) ? EncodedValue : null;
    }
}
