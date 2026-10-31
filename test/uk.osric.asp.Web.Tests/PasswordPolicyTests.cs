// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Users;

[TestFixture]
public sealed class PasswordPolicyTests {

    [TestCase(PasswordPolicy.MinimumLength)]
    [TestCase(PasswordPolicy.MinimumLength + 1)]
    [TestCase(PasswordPolicy.MaximumLength)]
    public void AcceptedLengthsPass(int length) {
        Assert.That(() => PasswordPolicy.Validate(new string('a', length)), Throws.Nothing);
    }

    [TestCase(0)]
    [TestCase(PasswordPolicy.MinimumLength - 1)]
    [TestCase(PasswordPolicy.MaximumLength + 1)]
    public void OtherLengthsAreRejected(int length) {
        _ = Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(new string('a', length)));
    }

    [Test]
    public void NullIsRejected() {
        _ = Assert.Throws<ArgumentNullException>(() => PasswordPolicy.Validate(null!));
    }
}
