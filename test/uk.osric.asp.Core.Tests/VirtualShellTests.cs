// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class VirtualShellTests {

    [TestCase("ls /blog", "ls", "/blog")]
    [TestCase("cat '/page with spaces'", "cat", "/page with spaces")]
    [TestCase("cat -n /blog", "cat", "/blog")]
    [TestCase("head -n 4 /blog", "head", "-n", "4", "/blog")]
    [TestCase("grep -i title /blog", "grep", "title", "/blog")]
    [TestCase("grep -n title", "grep", "title")]
    [TestCase("grep -n -i title /blog", "grep", "title", "/blog")]
    [TestCase("grep -i -n title /blog", "grep", "title", "/blog")]
    [TestCase("grep -i title", "grep", "title")]
    [TestCase("wc -l /blog", "wc", "-l", "/blog")]
    [TestCase("help", "help")]
    [TestCase("help cat", "help", "cat")]
    [TestCase("ls", "ls")]
    public void ParsesSupportedCommandsAndQuotedArguments(string command, params string[] expected) {
        VirtualShellParseResult result = VirtualShellParser.Parse(command);

        Assert.That(result.Error, Is.Null);
        Assert.That(new[] { result.Command!.Name }.Concat(result.Command.Arguments), Is.EqualTo(expected));
    }

    [TestCase("ls", false, false)]
    [TestCase("cat -n /blog", true, false)]
    [TestCase("grep title /blog", false, false)]
    [TestCase("grep -n title /blog", true, false)]
    [TestCase("grep -i title /blog", false, true)]
    [TestCase("grep -n -i title /blog", true, true)]
    [TestCase("grep -i -n title /blog", true, true)]
    public void OptionsAreRecordedAndTakenOutOfTheArguments(string command, bool numberLines, bool ignoreCase) {
        VirtualShellParseResult result = VirtualShellParser.Parse(command);

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Command!.NumberLines, Is.EqualTo(numberLines));
            Assert.That(result.Command.IgnoreCase, Is.EqualTo(ignoreCase));
            Assert.That(result.Command.Arguments, Has.None.StartsWith("-"),
                "an option is not one of the arguments the command is answered with");
        });
    }

    [Test]
    public void CommandsAreListedInOrdinalOrder() {
        Assert.That(VirtualShellParser.Commands,
            Is.EqualTo((string[])["cat", "find", "grep", "head", "help", "ls", "stat", "tail", "wc"]),
            "the help command's table is pinned to this list");
    }

    [TestCase("cat -n /vim.js", "cat", "/vim.js")]
    [TestCase("grep -n \"editor-page\" /vim.css", "grep", "editor-page", "/vim.css")]
    [TestCase("grep -n -i \"data-vim-only\" /vim-keys.js", "grep", "data-vim-only", "/vim-keys.js")]
    [TestCase("grep -n \"table\\|th\\b\\|\\.pages\\|listing\" /site.css", "grep", "table\\|th\\b\\|\\.pages\\|listing", "/site.css")]
    [TestCase("grep -n \"&mdash;\" /vim", "grep", "&mdash;", "/vim")]
    [TestCase("help", "help")]
    public void ParsesTheFormsTheAssistantTypedInProduction(string command, params string[] expected) {
        // From the live log (2026-11-10): of the 31 shell calls made there, 26 were refused. Some
        // were a host-shell form like one of these, and these run now; the rest are a pipe, an
        // option the shell does not have, or a command it does not answer.
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

    [TestCase("grep '&mdash;' /vim", "grep", "&mdash;", "/vim")]
    [TestCase("grep 'a|b' /vim", "grep", "a|b", "/vim")]
    [TestCase("grep \"a|b\" /vim", "grep", "a|b", "/vim")]
    [TestCase("grep a\\|b /vim", "grep", "a|b", "/vim")]
    [TestCase("grep 'a\\|b' /vim", "grep", "a\\|b", "/vim")]
    [TestCase("grep '$(' /vim.js", "grep", "$(", "/vim.js")]
    [TestCase("grep \"\\$HOME\" /vim", "grep", "$HOME", "/vim")]
    [TestCase("cat /my\\ page", "cat", "/my page")]
    [TestCase("cat '/page; rm /page'", "cat", "/page; rm /page")]
    [TestCase("grep pre\"fix\" /vim", "grep", "prefix", "/vim")]
    [TestCase("help 'cat'", "help", "cat")]
    public void QuotingAndEscapingMakeAPunctuationCharacterData(string command, params string[] expected) {
        VirtualShellParseResult result = VirtualShellParser.Parse(command);

        Assert.That(result.Error, Is.Null);
        Assert.That(new[] { result.Command!.Name }.Concat(result.Command.Arguments), Is.EqualTo(expected));
    }

    [TestCase("grep $HOME /vim")]
    [TestCase("grep \"$HOME\" /vim")]
    [TestCase("cat `pwd`")]
    [TestCase("grep \"$(id)\" /vim")]
    public void RefusesExpansionRatherThanPassingItThrough(string command) {
        string? error = VirtualShellParser.Parse(command).Error;

        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("no variable or command expansion"));
    }

    [Test]
    public void RefusesUnknownCommandsAndUnclosedQuotes() {
        Assert.Multiple(() => {
            Assert.That(VirtualShellParser.Parse("rm /page").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("cat 'unfinished").Error, Is.Not.Null);
            Assert.That(VirtualShellParser.Parse("cat \"unfinished").Error, Is.Not.Null);
        });
    }

    [Test]
    public void AnUnknownCommandIsRefusedAsThatCommandWhateverElseTheLineCarries() {
        // The shape the live log produced: the quoting is legal and the punctuation inside it is
        // data, so the only thing wrong with the line is the command, and that is what it says.
        VirtualShellParseResult result = VirtualShellParser.Parse(
            "awk '{o+=gsub(/{/,\"{\"); c+=gsub(/}/,\"}\")} END {print \"open=\" o, \"close=\" c}' /vim.js");

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error, Does.Contain("awk"));
            Assert.That(result.Error, Does.Not.Contain("pipe"));
            Assert.That(result.Error, Does.Not.Contain("separator"));
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
            Assert.That(VirtualShellParser.Parse("help cat dog").Error, Is.Not.Null);
        });
    }

    [Test]
    public void RefusesAnOptionTheCommandDoesNotHave() {
        Assert.Multiple(() => {
            Assert.That(VirtualShellParser.Parse("cat -n").Error, Is.Not.Null, "no key");
            Assert.That(VirtualShellParser.Parse("cat -i /page").Error, Is.Not.Null, "cat has no -i");
            Assert.That(VirtualShellParser.Parse("grep -n").Error, Is.Not.Null, "no query");
            Assert.That(VirtualShellParser.Parse("grep -i -i title").Error, Is.Not.Null, "a repeated option is left over");
            Assert.That(VirtualShellParser.Parse("grep -rn title").Error, Is.Not.Null, "options are not clustered");
            Assert.That(VirtualShellParser.Parse("grep -r title").Error, Is.Not.Null, "grep has no -r");
            Assert.That(VirtualShellParser.Parse("grep title -n").Error, Is.Not.Null, "an option must lead");
        });
    }

    [Test]
    public void EveryGrammarRefusalSaysWhereTheFormIsWrittenDown() {
        // Every refusal is a guess at a host shell, and the model has to be told how to find the
        // accepted form rather than being left to guess again.
        foreach (string command in new[] { "", "rm /page", "ls /blog | cat", "cat", "cat -i /page", "cat 'open", "grep $HOME /vim" }) {
            string? error = VirtualShellParser.Parse(command).Error;

            Assert.That(error, Is.Not.Null, command);
            Assert.That(error, Does.Contain("help"), command);
        }
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
