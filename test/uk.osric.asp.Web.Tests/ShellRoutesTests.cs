// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ShellRoutesTests {

    [Test]
    public void EveryReservedKeyHasARecordAndEveryRecordIsReserved() {
        string[] reserved = [.. ReservedPaths.Default.Select(key => PagePath.Normalize(key).Key)];
        string[] described = [.. ShellRoutes.All.Select(route => route.Path)];

        Assert.That(described, Is.EquivalentTo(reserved),
            "a new route adds its record here, and a retired one takes it away");
    }

    [Test]
    public void ARecordIsOneFactPerLine() {
        string[] lines = ShellRoutes.Text(PagePath.Normalize("/login")).Split('\n');

        Assert.Multiple(() => {
            Assert.That(lines, Has.Length.EqualTo(5), "so a line number from grep or read_page is stable");
            Assert.That(lines[0], Is.EqualTo("/login"));
            Assert.That(lines[1], Does.StartWith("shell file:"));
            Assert.That(lines[2], Does.StartWith("methods: "));
            Assert.That(lines[3], Does.StartWith("access: "));
            Assert.That(lines[4], Does.StartWith("description: "));
        });
    }

    [Test]
    public void EveryRecordHasOneLinePerField() {
        foreach (ShellRoute route in ShellRoutes.All) {
            Assert.Multiple(() => {
                Assert.That(route.Methods, Does.Not.Contain("\n"), route.Path);
                Assert.That(route.Access, Does.Not.Contain("\n"), route.Path);
                Assert.That(route.Description, Does.Not.Contain("\n"), route.Path);
                Assert.That(route.Path, Does.StartWith("/"));
            });
        }
    }

    [Test]
    public void EveryRecordReadsAsARecord() {
        foreach (ShellRoute route in ShellRoutes.All) {
            string text = ShellRoutes.Text(PagePath.Normalize(route.Path));

            Assert.That(text, Does.StartWith(route.Path + "\n"));
            Assert.That(text, Does.Contain("methods: " + route.Methods));
            Assert.That(text, Does.Contain("description: " + route.Description));
        }
    }

    [Test]
    public void AnUndescribedReservedKeyGetsTheFallbackRecord() {
        string text = ShellRoutes.Text(PagePath.Normalize("/extra"));

        Assert.Multiple(() => {
            Assert.That(text.Split('\n'), Has.Length.EqualTo(3));
            Assert.That(text, Does.StartWith("/extra\nshell file:"));
            Assert.That(text, Does.Contain("does not describe"));
        });
    }
}
