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
    public void ParsesSupportedCommandsAndQuotedArguments(string command, params string[] expected) {
        VirtualShellParseResult result = VirtualShellParser.Parse(command);

        Assert.That(result.Error, Is.Null);
        Assert.That(new[] { result.Command!.Name }.Concat(result.Command.Arguments), Is.EqualTo(expected));
    }

    [TestCase("ls /blog | cat")]
    [TestCase("cat /page; rm /page")]
    [TestCase("cat /page\nwrite_page")]
    [TestCase("cat /page > /host")]
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
            Assert.That(VirtualShellParser.Parse("grep -i").Error, Is.Not.Null);
        });
    }
}
