// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Shell;

[TestFixture]
public sealed class ShellSyntaxTests {

    private static readonly ShellSyntax s_ls = new(
        ['l', 'R', 'r'],
        [],
        [new ShellOperand("path", ShellOperandKind.Prefix, 0, int.MaxValue)]);

    private static readonly ShellSyntax s_grep = new(
        ['i', 'n', 'r', 'l', 'c'],
        [],
        [new ShellOperand("query", ShellOperandKind.Text), new ShellOperand("path", ShellOperandKind.Prefix, 0, int.MaxValue)]);

    private static readonly ShellSyntax s_head = new(
        [],
        [new ShellValueOption('n', "a number")],
        [new ShellOperand("key", ShellOperandKind.Path, 1, int.MaxValue)]);

    private static readonly ShellSyntax s_diff = new(
        ['b', 'u'],
        [new ShellValueOption('U', "a number")],
        [new ShellOperand("left", ShellOperandKind.Path), new ShellOperand("right", ShellOperandKind.Path)]);

    private static readonly ShellSyntax s_help = new(
        [],
        [],
        [new ShellOperand("command", ShellOperandKind.Text, 0, 1)]);

    [Test]
    public void FlagsClusterAndRepeat() {
        ShellCommandLine clustered = Match(s_ls, "ls -lRr /a");
        ShellCommandLine repeated = Match(s_ls, "ls -l -l /a");

        Assert.Multiple(() => {
            Assert.That(clustered.Has('l') && clustered.Has('R') && clustered.Has('r'), Is.True);
            Assert.That(repeated.Has('l'), Is.True);
            Assert.That(repeated.Arguments.Select(argument => argument.Word.Text), Is.EqualTo((string[])["/a"]));
        });
    }

    [TestCase("head -n5 /a", 5)]
    [TestCase("head -n 5 /a", 5)]
    [TestCase("head -n 0 /a", 0)]
    public void AValueMayAttachOrTakeTheNextWord(string line, int expected) {
        Assert.That(Match(s_head, line).Number('n'), Is.EqualTo(expected));
    }

    [Test]
    public void AValueOptionAndFlagsMixInAnyOrder() {
        ShellCommandLine attached = Match(s_diff, "diff -b -U2 /a /b");
        ShellCommandLine spaced = Match(s_diff, "diff -U 2 -b /a /b");

        foreach (ShellCommandLine line in new[] { attached, spaced }) {
            Assert.Multiple(() => {
                Assert.That(line.Has('b'), Is.True);
                Assert.That(line.Number('U'), Is.EqualTo(2));
                Assert.That(line.Arguments.Select(argument => argument.Word.Text), Is.EqualTo((string[])["/a", "/b"]));
            });
        }
    }

    [Test]
    public void NoValueIsNullRatherThanZero() {
        ShellCommandLine line = Match(s_head, "head /a");

        Assert.Multiple(() => {
            Assert.That(line.Number('n'), Is.Null);
            Assert.That(line.Arguments, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ARecordWithNoOperandsMatchesNothing() {
        ShellCommandLine line = Match(s_help, "help");

        Assert.That(line.Arguments, Is.Empty);
    }

    [Test]
    public void TwoDashesEndTheOptions() {
        ShellCommandLine line = Match(s_grep, "grep -- -foo /a");

        Assert.Multiple(() => {
            Assert.That(line.Arguments[0].Word.Text, Is.EqualTo("-foo"));
            Assert.That(line.Arguments[0].Kind, Is.EqualTo(ShellOperandKind.Text));
        });
    }

    [Test]
    public void OperandsTakeTheirDeclaredKindsInOrder() {
        ShellCommandLine line = Match(s_grep, "grep alpha /a /b");

        Assert.That(line.Arguments.Select(argument => (argument.Kind, argument.Word.Text)),
            Is.EqualTo(((ShellOperandKind, string)[])[(ShellOperandKind.Text, "alpha"), (ShellOperandKind.Prefix, "/a"), (ShellOperandKind.Prefix, "/b")]));
    }

    [Test]
    public void APrefixListMayBeEmptyAndALongOneIsFine() {
        Assert.Multiple(() => {
            Assert.That(Match(s_ls, "ls").Arguments, Is.Empty);
            Assert.That(Match(s_ls, "ls /a /b /c").Arguments, Has.Count.EqualTo(3));
        });
    }

    [TestCase("ls -x /a", "there is no '-x'")]
    [TestCase("head -n", "-n needs a number")]
    [TestCase("head -n two /a", "-n needs a number")]
    [TestCase("ls /a -l", "an option must come before the operands")]
    [TestCase("diff /a", "it needs two operands")]
    [TestCase("diff /a /b /c", "it needs two operands")]
    [TestCase("help a b", "it needs 0 operands and takes at most one operand")]
    public void RefusesWhatTheSyntaxDoesNotAllow(string line, string why) {
        ShellMatchResult result = Refuse(line);

        Assert.Multiple(() => {
            Assert.That(result.Error, Does.Contain(why));
            Assert.That(result.Error, Does.Contain("Run help"), "a refusal says where the form is written down");
            Assert.That(result.Error, Does.Contain(line.Split(' ')[0]), "and quotes the command");
        });
    }

    private static ShellCommandLine Match(ShellSyntax syntax, string line) {
        ShellLexResult lexed = ShellLine.Lex(line);
        Assert.That(lexed.Error, Is.Null, lexed.Error);
        ShellMatchResult result = syntax.Match([.. lexed.Words.Skip(1)], line);
        Assert.That(result.Error, Is.Null, result.Error);
        return result.Line!;
    }

    private static ShellMatchResult Refuse(string line) {
        ShellSyntax syntax = line.Split(' ')[0] switch {
            "ls" => s_ls,
            "grep" => s_grep,
            "head" => s_head,
            "diff" => s_diff,
            _ => s_help,
        };

        return syntax.Match([.. ShellLine.Lex(line).Words.Skip(1)], line);
    }
}
