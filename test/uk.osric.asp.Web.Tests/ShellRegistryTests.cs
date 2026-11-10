// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Shell;
using uk.osric.asp.Web.Assistant.Shell;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ShellRegistryTests {

    [Test]
    public void EveryAppletStartsItsUsageWithItsName() {
        foreach (ShellApplet applet in ShellRunner.Registry.All) {
            Assert.That(applet.Usage, Does.StartWith(applet.Name + " "), applet.Name);
        }
    }

    [Test]
    public void EveryOptionTheUsageNamesIsDeclaredAndEveryDeclaredOptionIsNamed() {
        foreach (ShellApplet applet in ShellRunner.Registry.All) {
            IEnumerable<char> named = applet.Usage
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim('[', ']', '(', ')', '<', '>', '.'))
                .Where(word => word.Length == 2 && word[0] == '-')
                .Select(word => word[1]);
            IEnumerable<char> declared = [.. applet.Syntax.Flags, .. applet.Syntax.Values.Select(value => value.Letter)];

            Assert.Multiple(() => {
                Assert.That(named, Is.EquivalentTo(declared), $"{applet.Name}: the usage and the syntax agree");
                Assert.That(applet.Description, Does.Not.Contain("\n"), $"{applet.Name}: one line");
                Assert.That(applet.Description, Does.EndWith("."), $"{applet.Name}: a sentence");
                Assert.That(applet.Notes ?? string.Empty, Does.Not.EndWith("\n"), $"{applet.Name}: no trailing break");
            });
        }
    }

    [Test]
    public void TheRegistryIsOrdinalAndFindsItsOwnApplets() {
        Assert.Multiple(() => {
            Assert.That(ShellRunner.Registry.All.Select(applet => applet.Name), Is.Ordered);
            Assert.That(ShellRunner.Registry.Find("ls"), Is.Not.Null);
            Assert.That(ShellRunner.Registry.Find("nosuchcommand"), Is.Null);
        });
    }

    [Test]
    public void EveryDeclaredFlagSurvivesTheParser() {
        // A usage line is what a caller types, so every one of its options has to be one the syntax
        // accepts, or the help text would describe a command the kernel refuses.
        foreach (ShellApplet applet in ShellRunner.Registry.All) {
            foreach (char flag in applet.Syntax.Flags) {
                ShellLexResult lexed = ShellLine.Lex($"{applet.Name} -{flag}");

                Assert.That(lexed.Error, Is.Null, $"{applet.Name} -{flag}");
                ShellMatchResult matched = applet.Syntax.Match([.. lexed.Words.Skip(1)], applet.Usage);
                Assert.That(matched.Error ?? string.Empty, Does.Not.Contain($"there is no '-{flag}'"),
                    $"{applet.Name} -{flag} is declared, so it must parse");
            }
        }
    }

    [Test]
    public async Task HelpListsEveryAppletAndDescribesOne() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string listed = await ShellKernelHarness.OutputAsync(pages.Editor, "help");
        string described = await ShellKernelHarness.OutputAsync(pages.Editor, "help ls");

        Assert.Multiple(() => {
            foreach (ShellApplet applet in ShellRunner.Registry.All) {
                Assert.That(listed, Does.Contain($"{applet.Usage}: {applet.Description}"), applet.Name);
            }
            Assert.That(listed, Does.Contain("grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]"),
                "a command that has not moved to an applet is still described");
            Assert.That(described, Does.StartWith("ls [-l] [-R] [-r] [path ...]: "));
        });
    }

    [Test]
    public async Task HelpNamesACommandItDoesNotAnswer() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "help nosuchcommand");

        Assert.That(refusal, Does.Contain("is not supported").And.Contain("Run help"));
    }

    [Test]
    public async Task AnOperatorIsRefusedAsNotThereYet() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        string refusal = await ShellKernelHarness.RefusalAsync(pages.Editor, "ls / | head");

        Assert.That(refusal, Does.Contain("yet").And.Contain("'|'"));
    }
}
