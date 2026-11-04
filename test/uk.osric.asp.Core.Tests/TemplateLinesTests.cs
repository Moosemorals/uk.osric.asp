// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class TemplateLinesTests {

    [TestCase("", 1)]
    [TestCase("a", 1)]
    [TestCase("a\n", 2)]
    [TestCase("a\nb", 2)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\rb", 2)]
    [TestCase("\n\n", 3)]
    [TestCase("a\r\n\r\n", 3)]
    public void CountMatchesTheBreaksTheTokenizerSees(string input, int expected) {
        Assert.That(TemplateLines.Count(input), Is.EqualTo(expected));
    }

    [Test]
    public void CountRefusesAMissingTemplate() {
        Assert.That(() => TemplateLines.Count(null!), Throws.ArgumentNullException);
    }
}
