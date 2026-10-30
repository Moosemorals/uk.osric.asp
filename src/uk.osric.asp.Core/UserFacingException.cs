// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>An exception whose message is safe to show to the user.</summary>
public sealed class UserFacingException : Exception {
    /// <summary>Creates an exception with a message approved for display to the user.</summary>
    /// <param name="message">The user-safe message.</param>
    public UserFacingException(string message) : base(message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}
