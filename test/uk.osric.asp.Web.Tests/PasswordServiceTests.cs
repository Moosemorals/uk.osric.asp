// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Web.Users;

[TestFixture]
public sealed class PasswordServiceTests {

    private const string Password = "correct horse battery staple";

    private readonly PasswordService _passwords = new(NullLogger<PasswordService>.Instance);

    // The shipped iteration count is a property of the product rather than of the test, and 600,000
    // of them cost a third of a second, so the fixture hashes the password it verifies against once.
    private string _passwordHash = string.Empty;

    [OneTimeSetUp]
    public void HashOnce() => _passwordHash = _passwords.Hash(User(string.Empty), Password);

    [Test]
    public void TheHashIsNotThePassword() {
        Assert.That(_passwordHash, Is.Not.EqualTo(Password));
        Assert.That(_passwordHash, Does.Not.Contain(Password));
    }

    [Test]
    public void TheRightPasswordVerifies() {
        LocalUser user = User(_passwordHash);

        Assert.That(_passwords.Verify(user, Password), Is.EqualTo(PasswordVerificationResult.Success));
    }

    [Test]
    public void TheWrongPasswordFails() {
        LocalUser user = User(_passwordHash);

        Assert.That(_passwords.Verify(user, "wrong password"), Is.EqualTo(PasswordVerificationResult.Failed));
    }

    [Test]
    public void TwoHashesOfTheSamePasswordDiffer() {
        string second = _passwords.Hash(User(string.Empty), Password);

        Assert.That(_passwordHash, Is.Not.EqualTo(second));
    }

    [Test]
    public void AMalformedHashFailsWithoutThrowing() {
        LocalUser user = User("not-a-valid-hash");

        Assert.That(_passwords.Verify(user, Password), Is.EqualTo(PasswordVerificationResult.Failed));
    }

    private static LocalUser User(string passwordHash) {
        return new LocalUser {
            UserName = "owner",
            PasswordHash = passwordHash,
            SecurityStamp = "stamp",
        };
    }
}
