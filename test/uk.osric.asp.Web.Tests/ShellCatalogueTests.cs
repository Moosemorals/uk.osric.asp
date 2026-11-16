// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;

/// <summary>The one table of the shell's routes: the record, and the key set a page may not claim.</summary>
[TestFixture]
public sealed class ShellCatalogueTests {

    [Test]
    public void EveryReservedKeyIsARouteAndEveryRouteIsReserved() {
        string[] reserved = [.. ReservedPaths.Default.Select(key => PagePath.Normalize(key).Key)];
        string[] routes = [.. ShellCatalogue.Routes.Select(route => route.Key)];

        Assert.That(routes, Is.EquivalentTo(reserved));
        Assert.That(routes, Is.Unique, "one row per route");
    }

    [Test]
    public void ARecordIsOneFactPerLine() {
        string[] lines = ShellCatalogue.Text(PagePath.Normalize("/login")).Split('\n');

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
        foreach (ShellRoute route in ShellCatalogue.Routes) {
            Assert.Multiple(() => {
                Assert.That(route.Methods, Does.Not.Contain("\n"), route.Key);
                Assert.That(route.Access, Does.Not.Contain("\n"), route.Key);
                Assert.That(route.Description, Does.Not.Contain("\n"), route.Key);
                Assert.That(route.Key, Does.StartWith("/"));
            });
        }
    }

    [Test]
    public void EveryRecordReadsAsARecord() {
        foreach (ShellRoute route in ShellCatalogue.Routes) {
            string text = ShellCatalogue.Text(PagePath.Normalize(route.Key));

            Assert.That(text, Does.StartWith(route.Key + "\n"));
            Assert.That(text, Does.Contain("methods: " + route.Methods));
            Assert.That(text, Does.Contain("description: " + route.Description));
        }
    }

    // The prose is the audience's sentence, with a note only where the route says more than who may
    // reach it.
    [Test]
    public void TheAccessSentenceFollowsTheAudience() {
        string[] derived = [.. ShellCatalogue.Routes
            .Where(route => route.AccessNote is null)
            .Select(route => route.Access)
            .Distinct()];

        Assert.That(derived, Is.SubsetOf(["anyone", "anyone who has not signed in", "the owner"]));
        Assert.That(ShellCatalogue.Routes.Single(route => route.Key == "/login").Access, Does.Contain("throttled"));
        Assert.That(ShellCatalogue.Routes.Single(route => route.Key == "/metrics").Access, Does.Contain("internal network"));
    }

    // The reserved set is the routes, so this cannot happen from the panel side; it is still the
    // shape the assistant gets rather than an exception if a key ever arrives without a record.
    [Test]
    public void AKeyTheTableDoesNotDescribeSaysSo() {
        string text = ShellCatalogue.Text(PagePath.Normalize("/extra"));

        Assert.That(text.Split('\n'), Has.Length.EqualTo(3));
        Assert.That(text, Does.StartWith("/extra\nshell file:"));
        Assert.That(text, Does.Contain("does not describe"));
    }
}
