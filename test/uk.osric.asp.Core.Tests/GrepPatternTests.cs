// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class GrepPatternTests {

    [TestCase("alpha", "alpha beta", true)]
    [TestCase("alpha", "ALPHA beta", false)]
    [TestCase("^import", "import x", true)]
    [TestCase("^import", "x import", false)]
    [TestCase("done$", "all done", true)]
    [TestCase("done$", "done all", false)]
    [TestCase("f.o", "foo", true)]
    [TestCase("[abc]x", "bx", true)]
    [TestCase("[abc]x", "dx", false)]
    [TestCase("[^abc]x", "dx", true)]
    public void MatchesTheRegularExpressionItWasGiven(string query, string line, bool expected) {
        Assert.That(Compile(query, ignoreCase: false).IsMatch(line), Is.EqualTo(expected));
    }

    [TestCase("a\\|b", "b", true, "an escaped pipe is either-this-or-that in a basic expression")]
    [TestCase("a\\|b", "a", true, "either side matches")]
    [TestCase("a\\|b", "c", false, "and nothing else does")]
    public void AlternativesAreWrittenTheWayBasicRegexWritesThem(string query, string line, bool expected, string why) {
        Assert.That(Compile(query, ignoreCase: false).IsMatch(line), Is.EqualTo(expected), why);
    }

    [TestCase("cost $5", "the cost $5 all in", true, "a $ away from the end of the query is the character")]
    [TestCase("x^2", "x^2 = 4", true, "and so is a ^ away from the start")]
    [TestCase("vim.js", "vimxjs", true, "a dot is a metacharacter, as it has always been")]
    [TestCase("vim\\.js", "vimxjs", false, "and an escaped dot is not")]
    [TestCase("a|b", "a|b", true, "a bare pipe is the literal character in a basic expression")]
    [TestCase("a|b", "a", false, "so neither side matches on its own")]
    [TestCase("(x)", "(x)", true, "so is a bare bracket")]
    [TestCase("a{2}", "a{2}", true, "and a bare brace")]
    [TestCase("a+b", "a+b", true, "and a bare plus")]
    [TestCase("a?b", "a?b", true, "and a bare question mark")]
    public void TreatsTheCharactersBasicRegexLeavesAloneAsLiteralText(string query, string line, bool expected, string why) {
        Assert.That(Compile(query, ignoreCase: false).IsMatch(line), Is.EqualTo(expected), why);
    }

    [Test]
    public void IgnoresCaseWhenAsked() {
        Assert.Multiple(() => {
            Assert.That(Compile("alpha", ignoreCase: true).IsMatch("ALPHA"), Is.True);
            Assert.That(Compile("^alpha$", ignoreCase: true).IsMatch("Alpha"), Is.True);
        });
    }

    [Test]
    public void AQueryWithNoMetacharacterIsAPlainSubstring() {
        // Not observable from outside, so this pins the meaning instead: nothing in it is syntax.
        GrepPattern pattern = Compile("the cost (roughly) is $5", ignoreCase: false);

        Assert.Multiple(() => {
            Assert.That(pattern.IsMatch("the cost (roughly) is $5, all in"), Is.True);
            Assert.That(pattern.IsMatch("the cost roughly is $5"), Is.False);
        });
    }

    [Test]
    public void RefusesAPatternItCannotRead() {
        Assert.Multiple(() => {
            Assert.That(TryCompile("a[bc", out string? error), Is.False, "a class that is not closed");
            Assert.That(error, Does.Contain("help grep"));
            Assert.That(TryCompile("trailing\\", out _), Is.False, "a backslash that escapes nothing");
            Assert.That(TryCompile("*x", out _), Is.False, "a quantifier with nothing to repeat");
            Assert.That(TryCompile("(a)\\1", out _), Is.False, "a backreference, which the linear matcher does not carry");
        });
    }

    private static GrepPattern Compile(string query, bool ignoreCase) {
        Assert.That(GrepPattern.TryCompile(query, ignoreCase, out GrepPattern? pattern, out string? error), Is.True, error);
        return pattern!;
    }

    private static bool TryCompile(string query, out string? error) =>
        GrepPattern.TryCompile(query, ignoreCase: false, out _, out error);
}
