// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Users;

[TestFixture]
public sealed class SecurityStampTests {

    [Test]
    public void StampsAreNotEmpty() {
        Assert.That(SecurityStamp.Create(), Is.Not.Empty);
    }

    [Test]
    public void StampsDiffer() {
        string first = SecurityStamp.Create();
        string second = SecurityStamp.Create();

        Assert.That(first, Is.Not.EqualTo(second));
    }
}
