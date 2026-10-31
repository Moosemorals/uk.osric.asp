// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

/// <summary>The pure rule for the key an account is stored under.</summary>
public static class UserName {

    /// <summary>The longest accepted user name, after normalisation.</summary>
    public const int MaximumLength = 64;

    /// <summary>Trims, case-folds, NFC-normalises and validates a user name.</summary>
    /// <param name="userName">The text submitted by a login form or the CLI.</param>
    /// <returns>The normalised key, in Unicode NFC.</returns>
    /// <exception cref="ArgumentException">The text is empty, too long or contains control characters.</exception>
    public static string Normalize(string userName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        // Lowercase first, then compose: a case mapping can change the composition, and the stored
        // key must be NFC so two spellings of the same name cannot become two accounts.
        string normalized = userName.Trim().ToLowerInvariant().Normalize();

        if (normalized.Length > MaximumLength) {
            throw new ArgumentException($"A user name must be at most {MaximumLength} characters.");
        }

        if (normalized.Any(char.IsControl)) {
            throw new ArgumentException("A user name must not contain control characters.");
        }

        return normalized;
    }
}
