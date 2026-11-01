// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

/// <summary>The PBKDF2 password hasher shared by the login endpoint and the CLI.</summary>
internal sealed class PasswordService : IPasswordService {

    private static readonly string DummyPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly PasswordHasher<LocalUser> _hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = 600_000 }));

    // Unknown users still pay for one verification, so the response time does not advertise which
    // names exist. The password is random and never leaves this object.
    private readonly LocalUser _dummy;

    public PasswordService() {
        LocalUser template = new() {
            UserName = "~dummy~",
            PasswordHash = string.Empty,
            SecurityStamp = string.Empty,
        };

        _dummy = new LocalUser {
            UserName = template.UserName,
            SecurityStamp = template.SecurityStamp,
            PasswordHash = _hasher.HashPassword(template, DummyPassword),
        };
    }

    /// <inheritdoc/>
    public string Hash(LocalUser user, string password) {
        ArgumentNullException.ThrowIfNull(user);

        return _hasher.HashPassword(user, password);
    }

    /// <inheritdoc/>
    public PasswordVerificationResult Verify(LocalUser user, string password) {
        ArgumentNullException.ThrowIfNull(user);

        try {
            return _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        } catch (FormatException) {
            // A corrupted hash cannot authenticate anyone; treat it as a failed password rather
            // than turning every login attempt into a 500.
            return PasswordVerificationResult.Failed;
        }
    }

    /// <inheritdoc/>
    public bool VerifyDummy(string password) {
        _ = _hasher.VerifyHashedPassword(_dummy, _dummy.PasswordHash, password);
        return false;
    }
}
