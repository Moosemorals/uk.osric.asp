// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class GeneratedSourceTests {

    [Test]
    public void TheGeneratedTextIsKeptAsItWasGiven() {
        GeneratedSource source = new("public sealed class Handler { }");

        Assert.That(source.Text, Is.EqualTo("public sealed class Handler { }"));
    }

    [TestCase("")]
    [TestCase("  \n\t")]
    public void TextWithNothingInItIsRejected(string text) {
        Assert.That(() => new GeneratedSource(text), Throws.ArgumentException);
    }
}
