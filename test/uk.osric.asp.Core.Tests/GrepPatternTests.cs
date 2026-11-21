// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

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
            Assert.That(error, Is.Not.Empty, "the compiler gives its reason");
            Assert.That(error, Does.Not.Contain("help grep"), "and leaves the advice to whoever teaches the syntax");
            Assert.That(TryCompile("trailing\\", out _), Is.False, "a backslash that escapes nothing");
            Assert.That(TryCompile("*x", out _), Is.False, "a quantifier with nothing to repeat");
            Assert.That(TryCompile("(a)\\1", out _), Is.False, "a backreference, which the linear matcher does not carry");
        });
    }

    [Test]
    public void ALiteralIsTheCharactersEvenWhenTheyLookLikeSyntax() {
        GrepPattern literal = CompileAs("foo.", literal: true);
        GrepPattern expression = CompileAs("foo.", literal: false);

        Assert.Multiple(() => {
            Assert.That(literal.IsMatch("a foo. b"), Is.True, "the dot is a dot");
            Assert.That(literal.IsMatch("a fooX b"), Is.False);
            Assert.That(expression.IsMatch("a fooX b"), Is.True, "the basic expression reads the dot as syntax");
        });
    }

    [Test]
    public void ABasicExpressionWithNoMetacharacterIsStillATheText() {
        GrepPattern expression = CompileAs("(x)", literal: false);

        Assert.Multiple(() => {
            Assert.That(expression.IsMatch("(x)"), Is.True, "a bracket is a literal character in this language");
            Assert.That(expression.IsMatch("x"), Is.False);
        });
    }

    [Test]
    public void TryMatchReportsWhereTheMatchStartsAndHowLongItIs() {
        GrepPattern literal = Compile("needle", ignoreCase: false);
        GrepPattern expression = Compile("n..dle", ignoreCase: false);

        Assert.Multiple(() => {
            Assert.That(literal.TryMatch("a needle here", out int literalIndex, out int literalLength), Is.True);
            Assert.That((literalIndex, literalLength), Is.EqualTo((2, 6)));
            Assert.That(expression.TryMatch("a needle here", out int expressionIndex, out int expressionLength), Is.True);
            Assert.That((expressionIndex, expressionLength), Is.EqualTo((2, 6)));
            Assert.That(literal.TryMatch("nothing", out int missingIndex, out int missingLength), Is.False);
            Assert.That((missingIndex, missingLength), Is.EqualTo((0, 0)));
        });
    }

    [Test]
    public void APatternThatCanMatchNothingAnswersAZeroLengthPosition() {
        GrepPattern expression = Compile("a*", ignoreCase: false);

        Assert.Multiple(() => {
            Assert.That(expression.TryMatch("bbb", out int index, out int length), Is.True);
            Assert.That((index, length), Is.EqualTo((0, 0)));
        });
    }

    private static GrepPattern Compile(string query, bool ignoreCase) {
        Assert.That(GrepPattern.TryCompile(query, ignoreCase, out GrepPattern? pattern, out string? error), Is.True, error);
        return pattern!;
    }

    private static GrepPattern CompileAs(string query, bool literal) {
        Assert.That(
            GrepPattern.TryCompile(query, literal, ignoreCase: false, out GrepPattern? pattern, out string? error),
            Is.True, error);
        return pattern!;
    }

    private static bool TryCompile(string query, out string? error) =>
        GrepPattern.TryCompile(query, ignoreCase: false, out _, out error);
}
