// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;

/// <summary>The menus the table draws: one per section, in table order, filtered by audience.</summary>
[TestFixture]
public sealed class ShellMenusTests {

    private const string EditorPath = "/pages";

    private static IReadOnlyList<ShellLink> All => ShellCatalogue.Links("/", string.Empty, EditorPath);

    // The table is only worth having if a link leads somewhere the site answers: a shell route, the
    // editor's configured key, or a page the release ships.
    [Test]
    public void EveryAdministrationLinkLeadsSomewhereTheSiteAnswers() {
        ReservedPaths reserved = new(ReservedPaths.Default);

        foreach (ShellLink link in All.Where(link => link.Section == ShellSection.Administration)) {
            string target = Target(link.Href);
            bool answered = reserved.IsReserved(PagePath.Normalize(target))
                || string.Equals(target, EditorPath, StringComparison.Ordinal)
                || TestOptions.Seeds.Find(PagePath.Normalize(target)) is not null;
            Assert.That(answered, Is.True, $"{link.Text} leads to {link.Href}");
        }
    }

    // A landmark is a set of links: two entries with one href would draw the same destination twice,
    // and two with one label would read as the same item to a screen reader.
    [Test]
    public void AMenuHasNoRepeatedHrefOrLabel() {
        foreach (ShellSection section in Enum.GetValues<ShellSection>()) {
            ShellLink[] links = [.. All.Where(link => link.Section == section)];

            Assert.Multiple(() => {
                Assert.That(links.Select(link => Target(link.Href)), Is.Unique, section.ToString());
                Assert.That(links.Select(link => link.Text), Is.Unique, section.ToString());
            });
        }
    }

    // Two menus, two orders: /admin is first in the administration menu and fifth in the site menu,
    // and the editor stands beside the admin tools and beside the assistant.
    [Test]
    public void BothMenusReadInTheirOwnOrder() {
        string[] site = [.. All.Where(link => link.Section == ShellSection.Site).Select(link => link.Text)];
        string[] administration = [.. All.Where(link => link.Section == ShellSection.Administration).Select(link => link.Text)];

        Assert.Multiple(() => {
            Assert.That(site, Is.EqualTo(["Home", "Sign in", "Assistant", "Pages", "Admin", "Sign out"]));
            Assert.That(administration, Is.EqualTo([
                "Administration", "Import pages", "Export pages", "Changelist", "Restore seeds",
                "Apply seed updates", "Reference", "Copyright and licences", "Status", "Page list", "Help",
            ]));
        });
    }

    [Test]
    public void AnOwnerIsNotOfferedTheSignInLinkAndAVisitorIsOfferedNothingElse() {
        Assert.Multiple(() => {
            Assert.That(ShellCatalogue.IsFor(Link("Sign in", ShellAudience.Anonymous), authenticated: false), Is.True);
            Assert.That(ShellCatalogue.IsFor(Link("Sign in", ShellAudience.Anonymous), authenticated: true), Is.False);
            Assert.That(ShellCatalogue.IsFor(Link("Admin", ShellAudience.Owner), authenticated: true), Is.True);
            Assert.That(ShellCatalogue.IsFor(Link("Admin", ShellAudience.Owner), authenticated: false), Is.False);
            Assert.That(ShellCatalogue.IsFor(Link("Help", ShellAudience.Everyone), authenticated: false), Is.True);
            Assert.That(ShellCatalogue.IsFor(Link("Help", ShellAudience.Everyone), authenticated: true), Is.True);
        });
    }

    [Test]
    public void TheSiteMenuIsHomeAndSignInForAStranger() {
        IReadOnlyList<NavLink> links = SiteNavigation.Links(false, "/", "?q=1", EditorPath);

        Assert.Multiple(() => {
            Assert.That(links.Select(link => link.Text), Is.EqualTo(["Home", "Sign in"]));
            Assert.That(links[1].Href, Does.StartWith("/login?returnUrl="));
            Assert.That(links[1].Href, Does.Contain("%2F%3Fq%3D1"), "the way back survives the hop");
        });
    }

    [Test]
    public void TheSiteMenuIsTheOwnersToolsOnceTheySignIn() {
        IReadOnlyList<NavLink> links = SiteNavigation.Links(true, "/pages", string.Empty, EditorPath);

        Assert.That(links.Select(link => link.Text),
            Is.EqualTo(["Home", "Assistant", "Pages", "Admin", "Sign out"]));
        Assert.That(links.Single(link => link.Text == "Pages").IsCurrent, Is.True);
    }

    private static ShellLink Link(string text, ShellAudience audience) =>
        All.Single(link => string.Equals(link.Text, text, StringComparison.Ordinal) && link.Audience == audience);

    private static string Target(string href) {
        int query = href.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? href[..query] : href;
    }
}
