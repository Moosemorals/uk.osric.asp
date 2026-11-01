// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using System.Security.Cryptography;

/// <summary>Creates the value that revokes authentication cookies when an account changes.</summary>
internal static class SecurityStamp {

    /// <summary>Creates a new random stamp.</summary>
    public static string Create() {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
