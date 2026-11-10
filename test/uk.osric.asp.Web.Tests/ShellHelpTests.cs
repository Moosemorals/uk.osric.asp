// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class ShellHelpTests {

    [Test]
    public void EveryCommandHasAnEntryAndEveryEntryIsACommand() {
        Assert.That(ShellHelp.All.Select(entry => entry.Name), Is.EquivalentTo(VirtualShellParser.Commands),
            "a new command adds its entry here, and a retired one takes it away");
    }

    [Test]
    public void EntriesAreInOrdinalOrder() {
        Assert.That(ShellHelp.All.Select(entry => entry.Name), Is.Ordered);
    }

    [Test]
    public void EveryEntryIsOneLinePerField() {
        foreach (ShellHelpEntry entry in ShellHelp.All) {
            Assert.Multiple(() => {
                Assert.That(entry.Usage, Does.StartWith(entry.Name + " ").Or.EqualTo(entry.Name), entry.Name);
                Assert.That(entry.Usage, Does.Not.Contain("\n"), entry.Name);
                Assert.That(entry.Description, Does.Not.Contain("\n"), entry.Name);
                Assert.That(entry.Description, Does.EndWith("."), entry.Name);
            });
        }
    }

    [Test]
    public void TheWholeTableIsThePrefaceAndOneLinePerCommand() {
        string[] lines = ShellHelp.Text().Split('\n');
        int preface = ShellHelp.Preface.Split('\n').Length;

        Assert.Multiple(() => {
            Assert.That(lines, Has.Length.EqualTo(ShellHelp.All.Count + preface + 1),
                "the preface, a blank line, then one line per command");
            Assert.That(lines[0], Does.StartWith("A read-only shell"));
            Assert.That(lines[preface], Is.Empty);
            Assert.That(lines[preface + 1], Does.StartWith("cat "));
        });
    }

    [Test]
    public void OneCommandIsItsOwnLine() {
        Assert.That(ShellHelp.TryGet("grep", out ShellHelpEntry? entry), Is.True);
        Assert.That(ShellHelp.Text(entry), Is.EqualTo(entry!.Usage + ": " + entry.Description));
        Assert.That(ShellHelp.Text(entry), Does.Not.Contain("\n"));
    }

    [Test]
    public void AWordThatIsNotACommandIsNotHelped() {
        Assert.That(ShellHelp.TryGet("rm", out ShellHelpEntry? entry), Is.False);
        Assert.That(entry, Is.Null);
    }
}
