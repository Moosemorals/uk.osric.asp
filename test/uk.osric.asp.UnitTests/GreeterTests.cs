// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.UnitTests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public class GreeterTests {
    [Test]
    public void Names_the_visitor() {
        Greeting greeting = Greeter.For("Ada");

        Assert.That(greeting.Text, Is.EqualTo("Hello, Ada!"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Greets_the_world_when_no_usable_name_is_given(string? name) {
        Greeting greeting = Greeter.For(name);

        Assert.That(greeting.Text, Is.EqualTo("Hello, world!"));
    }

    [TestCase("  Ada  ", "Hello, Ada!")]
    public void Ignores_surrounding_whitespace(string name, string expected) {
        Greeting greeting = Greeter.For(name);

        Assert.That(greeting.Text, Is.EqualTo(expected));
    }
}
