// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using Microsoft.AspNetCore.Identity;

/// <summary>Hashes passwords and checks a candidate against a stored hash.</summary>
public interface IPasswordService {

    /// <summary>Hashes a password for an account.</summary>
    /// <param name="user">The account the hash belongs to.</param>
    /// <param name="password">The plaintext password.</param>
    string Hash(LocalUser user, string password);

    /// <summary>Checks a candidate password against the account's stored hash.</summary>
    /// <param name="user">The account to check.</param>
    /// <param name="password">The candidate password.</param>
    /// <returns>
    /// <see cref="PasswordVerificationResult.Failed"/> for a wrong password or a malformed hash;
    /// otherwise the hasher's result. Callers rehash on
    /// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>.
    /// </returns>
    PasswordVerificationResult Verify(LocalUser user, string password);

    /// <summary>Runs a verification even though the account is unknown, then reports failure.</summary>
    /// <param name="password">The candidate password.</param>
    /// <returns>Always <see langword="false"/>; the point is the time spent hashing.</returns>
    bool VerifyDummy(string password);
}
