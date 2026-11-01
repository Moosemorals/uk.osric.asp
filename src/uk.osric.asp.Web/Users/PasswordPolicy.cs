// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

/// <summary>The rule applied when a password is set.</summary>
internal static class PasswordPolicy {

    /// <summary>The shortest accepted password.</summary>
    public const int MinimumLength = 12;

    /// <summary>The longest accepted password, so a login cannot spend unbounded time hashing.</summary>
    public const int MaximumLength = 1024;

    /// <summary>Validates a candidate password.</summary>
    /// <param name="password">The password to test.</param>
    /// <exception cref="ArgumentException">The password is outside the accepted length.</exception>
    public static void Validate(string password) {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length < MinimumLength) {
            throw new ArgumentException($"A password must be at least {MinimumLength} characters.");
        }

        if (password.Length > MaximumLength) {
            throw new ArgumentException($"A password must be at most {MaximumLength} characters.");
        }
    }
}
