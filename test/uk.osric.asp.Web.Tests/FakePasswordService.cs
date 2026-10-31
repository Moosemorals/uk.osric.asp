// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Identity;
using uk.osric.asp.Web.Users;

/// <summary>A fast password double; the real hasher is exercised by <see cref="PasswordServiceTests"/>.</summary>
internal sealed class FakePasswordService : IPasswordService {

    /// <summary>The result a correct password produces.</summary>
    public PasswordVerificationResult Result { get; set; } = PasswordVerificationResult.Success;

    public string Hash(LocalUser user, string password) {
        return "hash:" + password;
    }

    public PasswordVerificationResult Verify(LocalUser user, string password) {
        return string.Equals(user.PasswordHash, Hash(user, password), StringComparison.Ordinal)
            ? Result
            : PasswordVerificationResult.Failed;
    }

    public bool VerifyDummy(string password) {
        return false;
    }
}
