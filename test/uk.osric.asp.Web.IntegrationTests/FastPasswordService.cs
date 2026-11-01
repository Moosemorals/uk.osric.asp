// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;
using uk.osric.asp.Web.Users;

/// <summary>A hasher fast enough that a sign-in does not dominate the test that needs it.</summary>
/// <remarks>
/// The real <c>PasswordService</c> spends about half a second per verification at 600,000 PBKDF2
/// iterations, which is right for production and wrong for the twenty-odd sign-ins these fixtures
/// make to reach the page they are actually about. <c>EditorAuthenticationTests</c> keeps the real
/// hasher, because proving it is wired into the host is what that fixture is for.
/// </remarks>
internal sealed class FastPasswordService : IPasswordService {

    // Deliberately unsalted: this is a stand-in, not a hasher, so it must only be consistent with
    // itself. A salt drawn from the account would break the fixtures that hash a template account and
    // store the result on the real one, which the production hasher tolerates because it salts itself.
    public string Hash(LocalUser user, string password) {
        ArgumentNullException.ThrowIfNull(password);

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    public PasswordVerificationResult Verify(LocalUser user, string password) {
        ArgumentNullException.ThrowIfNull(password);

        return string.Equals(user.PasswordHash, Hash(user, password), StringComparison.Ordinal)
            ? PasswordVerificationResult.Success
            : PasswordVerificationResult.Failed;
    }

    public bool VerifyDummy(string password) {
        return false;
    }
}
