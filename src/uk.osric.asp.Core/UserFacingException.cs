// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>An exception whose message is safe to show to the user.</summary>
/// <remarks>
/// This is the only signal the shell trusts for display: every other exception is reported with a
/// generic message, because its text routinely contains paths, connection strings or page source.
/// The runtime unwraps the reflection wrapper around a page's exception, so a page that throws one of
/// these loses neither its message nor its stack.
/// </remarks>
public sealed class UserFacingException : Exception {
    /// <summary>Creates an exception with a message approved for display to the user.</summary>
    /// <param name="message">The user-safe message.</param>
    public UserFacingException(string message) : base(message) {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}
