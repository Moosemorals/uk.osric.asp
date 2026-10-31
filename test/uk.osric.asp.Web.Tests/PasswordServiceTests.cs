// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Identity;
using NUnit.Framework;
using uk.osric.asp.Web.Users;

[TestFixture]
public sealed class PasswordServiceTests {

    private const string Password = "correct horse battery staple";

    private readonly PasswordService _passwords = new();

    [Test]
    public void TheHashIsNotThePassword() {
        LocalUser user = User(string.Empty);

        string hash = _passwords.Hash(user, Password);

        Assert.That(hash, Is.Not.EqualTo(Password));
        Assert.That(hash, Does.Not.Contain(Password));
    }

    [Test]
    public void TheRightPasswordVerifies() {
        LocalUser user = User(_passwords.Hash(User(string.Empty), Password));

        Assert.That(_passwords.Verify(user, Password), Is.EqualTo(PasswordVerificationResult.Success));
    }

    [Test]
    public void TheWrongPasswordFails() {
        LocalUser user = User(_passwords.Hash(User(string.Empty), Password));

        Assert.That(_passwords.Verify(user, "wrong password"), Is.EqualTo(PasswordVerificationResult.Failed));
    }

    [Test]
    public void TwoHashesOfTheSamePasswordDiffer() {
        LocalUser user = User(string.Empty);
        string first = _passwords.Hash(user, Password);
        string second = _passwords.Hash(user, Password);

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void AMalformedHashFailsWithoutThrowing() {
        LocalUser user = User("not-a-valid-hash");

        Assert.That(_passwords.Verify(user, Password), Is.EqualTo(PasswordVerificationResult.Failed));
    }

    [Test]
    public void TheDummyVerificationAlwaysFails() {
        Assert.That(_passwords.VerifyDummy(Password), Is.False);
    }

    private static LocalUser User(string passwordHash) {
        return new LocalUser {
            UserName = "owner",
            PasswordHash = passwordHash,
            SecurityStamp = "stamp",
        };
    }
}
