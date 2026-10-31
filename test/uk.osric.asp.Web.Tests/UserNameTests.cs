// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Users;

[TestFixture]
public sealed class UserNameTests {

    [TestCase("Osric", "osric")]
    [TestCase("  osric  ", "osric")]
    [TestCase("OSRIC", "osric")]
    public void NamesAreTrimmedAndCaseFolded(string input, string expected) {
        Assert.That(UserName.Normalize(input), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void MissingNamesAreRejected(string input) {
        _ = Assert.Throws<ArgumentException>(() => UserName.Normalize(input));
    }

    [Test]
    public void NullIsRejected() {
        _ = Assert.Throws<ArgumentNullException>(() => UserName.Normalize(null!));
    }

    [Test]
    public void ComposedAndDecomposedNamesAreTheSame() {
        string composed = UserName.Normalize("José");
        string decomposed = UserName.Normalize("Jose\u0301");

        Assert.That(decomposed, Is.EqualTo(composed));
        Assert.That(decomposed, Is.EqualTo("josé"));
    }

    [Test]
    public void NormalisedNamesAreInNfc() {
        string normalized = UserName.Normalize("Jose\u0301");

        Assert.That(normalized.Normalize(), Is.EqualTo(normalized));
    }

    [Test]
    public void OverlongNamesAreRejected() {
        string name = new('a', UserName.MaximumLength + 1);

        _ = Assert.Throws<ArgumentException>(() => UserName.Normalize(name));
    }

    [Test]
    public void ControlCharactersAreRejected() {
        _ = Assert.Throws<ArgumentException>(() => UserName.Normalize("osric\u0001"));
    }

    [Test]
    public void TheMaximumLengthIsAccepted() {
        string name = new('a', UserName.MaximumLength);

        Assert.That(UserName.Normalize(name), Has.Length.EqualTo(UserName.MaximumLength));
    }
}
