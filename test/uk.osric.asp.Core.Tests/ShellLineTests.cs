// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Shell;

[TestFixture]
public sealed class ShellLineTests {

    [TestCase("ls /blog", 2)]
    [TestCase("  ls   /blog  ", 2)]
    [TestCase("cat 'two words'", 2)]
    [TestCase("cat 'a word' \"and another\"", 3)]
    [TestCase("grep", 1)]
    public void SplitsOnWhitespaceOutsideQuotes(string line, int words) {
        ShellLexResult result = ShellLine.Lex(line);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Words, Has.Count.EqualTo(words));
    }

    [TestCase("cat 'a b'", "a b")]
    [TestCase("cat \"a b\"", "a b")]
    [TestCase("cat a\\ b", "a b")]
    [TestCase("cat '/page; rm /page'", "/page; rm /page")]
    [TestCase("grep '&mdash;'", "&mdash;")]
    [TestCase("cat pre\"fix\"", "prefix")]
    [TestCase("cat ''", "")]
    public void AppliesQuotesAndEscapesToTheWord(string line, string expected) {
        ShellLexResult result = ShellLine.Lex(line);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Words[^1].Text, Is.EqualTo(expected));
    }

    [Test]
    public void QuotesKeepWorkingWhenTheyAreInsideAWord() {
        ShellWord word = ShellLine.Lex("cat pre\"fix\"post").Words[^1];

        Assert.Multiple(() => {
            Assert.That(word.Text, Is.EqualTo("prefixpost"));
            Assert.That(word.Segments.Select(segment => segment.Text), Is.EqualTo(["pre", "fix", "post"]));
            Assert.That(word.Segments.Select(segment => segment.IsLiteral), Is.EqualTo([false, true, false]));
        });
    }

    [Test]
    public void KeepsAnOperatorsPlaceInTheLine() {
        ShellLexResult result = ShellLine.Lex("grep foo /a | head -n 2");

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Tokens, Has.Count.EqualTo(7), "four words, an operator, two words");
            Assert.That(result.Operator, Is.EqualTo(new ShellOperator('|')));
            Assert.That(result.Words.Select(word => word.Text),
                Is.EqualTo(["grep", "foo", "/a", "head", "-n", "2"]));
        });
    }

    [TestCase("cat a > b", '>')]
    [TestCase("cat a < b", '<')]
    [TestCase("cat a ; b", ';')]
    [TestCase("cat a & b", '&')]
    [TestCase("cat a\nb", '\n')]
    [TestCase("head -n 2 /a 2>/dev/null", '>')]
    public void ReportsEveryOperatorAsItsOwnToken(string line, char expected) {
        Assert.That(ShellLine.Lex(line).Operator, Is.EqualTo(new ShellOperator(expected)));
    }

    [Test]
    public void AnOperatorInsideQuotesIsTheCharacter() {
        ShellLexResult result = ShellLine.Lex("grep 'a|b' /vim");

        Assert.Multiple(() => {
            Assert.That(result.Operator, Is.Null);
            Assert.That(result.Words[1].Text, Is.EqualTo("a|b"));
        });
    }

    [Test]
    public void KnowsWhichWildcardsTheLineMeant() {
        Assert.Multiple(() => {
            Assert.That(ShellLine.Lex("ls /vim/*.js").Words[1].HasWildcard, Is.True);
            Assert.That(ShellLine.Lex("ls /vim/?.js").Words[1].HasWildcard, Is.True);
            Assert.That(ShellLine.Lex("ls '/vim/*.js'").Words[1].HasWildcard, Is.False, "a quoted star is a star");
            Assert.That(ShellLine.Lex("ls /vim/\\*.js").Words[1].HasWildcard, Is.False, "and so is an escaped one");
            Assert.That(ShellLine.Lex("ls '/vim/*.js'").Words[1].Text, Is.EqualTo("/vim/*.js"));
        });
    }

    [TestCase("cat 'unfinished")]
    [TestCase("cat \"unfinished")]
    [TestCase("cat /page\\")]
    [TestCase("cat $HOME")]
    [TestCase("cat \"$HOME\"")]
    [TestCase("cat `pwd`")]
    public void RefusesWhatThereIsNoWayToRead(string line) {
        ShellLexResult result = ShellLine.Lex(line);

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Not.Null, line);
            Assert.That(result.Error, Does.Contain("help"), line);
        });
    }

    [Test]
    public void AQuotedExpansionCharacterIsData() {
        ShellLexResult result = ShellLine.Lex("grep '$HOME' /page");

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Words[1].Text, Is.EqualTo("$HOME"));
        });
    }

    [Test]
    public void AnEmptyLineIsNoTokens() {
        Assert.That(ShellLine.Lex("   ").Tokens, Is.Empty);
    }
}
