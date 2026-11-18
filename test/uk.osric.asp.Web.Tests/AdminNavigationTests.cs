// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Admin;

/// <summary>The administration landmark the shell's own tools share.</summary>
[TestFixture]
public sealed class AdminNavigationTests {

    [Test]
    public void TheToolBeingShownIsTheCurrentItem() {
        MarkupReader nav = MarkupReader.Parse(AdminNavigation.Nav(TestHttp.CreateContext(null, "GET", "/admin/import")));

        Assert.Multiple(() => {
            Assert.That(nav.Elements("nav").Single().Attribute("aria-label"), Is.EqualTo(AdminNavigation.Label));
            Assert.That(Current(nav), Is.EqualTo("Import pages"));
        });
    }

    // The index is a view of its own: it is not the place being shown while a tool is.
    [Test]
    public void TheAdministrationIndexIsNotCurrentFromATool() {
        MarkupReader nav = MarkupReader.Parse(AdminNavigation.Nav(TestHttp.CreateContext(null, "GET", "/admin/import/1")));

        Assert.That(nav.Elements("span").Select(span => span.Text.Trim()), Does.Not.Contain("Administration"));
    }

    [Test]
    public void TheIndexIsCurrentAtTheRoot() {
        MarkupReader nav = MarkupReader.Parse(AdminNavigation.Nav(TestHttp.CreateContext(null, "GET", "/admin")));

        Assert.That(Current(nav), Is.EqualTo("Administration"));
    }

    [Test]
    public void EveryToolInTheSectionIsOffered() {
        MarkupReader nav = MarkupReader.Parse(AdminNavigation.Nav(TestHttp.CreateContext(null, "GET", "/admin")));

        Assert.That(nav.Elements("a").Select(link => link.Text.Trim()), Does.Contain("Status").And.Contain("Help"));
    }

    private static string Current(MarkupReader nav) =>
        nav.Elements("span").Single(span => string.Equals(span.Attribute("aria-current"), "true", StringComparison.Ordinal))
            .Text.Trim();
}
