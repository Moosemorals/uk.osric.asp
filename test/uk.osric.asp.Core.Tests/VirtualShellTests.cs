// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class VirtualShellTests {

    [TestCase("ls /blog", "ls", "/blog")]
    [TestCase("cat '/page with spaces'", "cat", "/page with spaces")]
    [TestCase("head -n 4 /blog", "head", "-n", "4", "/blog")]
    [TestCase("grep -i title /blog", "grep", "-i", "title", "/blog")]
    [TestCase("grep -i title", "grep", "-i", "title")]
    [TestCase("wc -l /blog", "wc", "-l", "/blog")]
    [TestCase("ls", "ls")]
    public void ParsesSupportedCommandsAndQuotedArguments(string command, params string[] expected) {
        VirtualShellParseResult result = VirtualShellParser.Parse(command);

        Assert.That(result.Error, Is.Null);
        Assert.That(new[] { result.Command!.Name }.Concat(result.Command.Arguments), Is.EqualTo(expected));
    }

    [TestCase("ls /blog | cat")]
    [TestCase("cat /page; rm /page")]
    [TestCase("cat /page\nwrite_page")]
    [TestCase("cat /page > /host")]
    [TestCase("ls |cat")]
    public void RefusesShellMetacharactersAndNewlines(string command) {
        Assert.That(VirtualShellParser.Parse(command).Error, Is.Not.Null);
    }

    [Test]
    public void RefusesUnknownCommandsAndUnclosedQuotes() {
        Assert.Multiple(() => {
            Assert.That(VirtualShellParser.Parse("rm /page").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("cat 'unfinished").Error, Is.Not.Null);
        });
    }

    [Test]
    public void RefusesWrongArityAndInvalidLineCounts() {
        Assert.Multiple(() => {
            Assert.That(VirtualShellParser.Parse("cat").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("head -n zero /page").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("head -n 0 /page").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("head 4 /page").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("head -n 4").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("grep -i").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("grep a b c").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("wc -l").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("wc /page").Error, Is.Not.Null);
        });
    }

    [Test]
    public void RepeatedWhitespaceSeparatesTokensOnce() {
        VirtualShellParseResult result = VirtualShellParser.Parse("  ls   /blog");

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Command!.Name, Is.EqualTo("ls"));
        Assert.That(result.Command.Arguments, Is.EqualTo((string[])["/blog"]));
    }

    [Test]
    public void AnEmptyCommandIsRefused() {
        Assert.That(VirtualShellParser.Parse("").Error, Is.Not.Null);
    }

    [Test]
    public void ANullCommandIsRefused() {
        Assert.That(() => VirtualShellParser.Parse(null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("text"));
    }
}
