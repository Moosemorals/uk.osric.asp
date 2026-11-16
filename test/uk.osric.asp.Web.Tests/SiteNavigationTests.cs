// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Html;

/// <summary>The one way home a shell document carries.</summary>
/// <remarks>
/// The shell's documents are bootstrap surfaces, so their menu is home and, under <c>/admin</c>,
/// the admin index. The site menu a visitor sees is the stored layout's, and it is not this list.
/// </remarks>
[TestFixture]
public sealed class SiteNavigationTests {

    private static readonly (string Href, string Text)[] Footer = [("/about", "About"), ("/privacy", "Privacy policy")];

    [TestCase("/")]
    [TestCase("/login")]
    [TestCase("/status")]
    [TestCase("/assistant")]
    [TestCase("/nope")]
    public void AShellDocumentCarriesHomeAndNothingElse(string path) {
        Assert.That(Texts(path), Is.EqualTo(["Home"]));
    }

    [TestCase("/admin")]
    [TestCase("/admin/import")]
    [TestCase("/admin/import/1")]
    [TestCase("/admin/export")]
    public void AnAdminRouteCarriesAdminBesideHome(string path) {
        Assert.That(Texts(path), Is.EqualTo(["Home", "Admin"]));
    }

    [Test]
    public void AdminIsNeverOfferedOutsideItsPrefix() {
        Assert.That(Texts("/administrator"), Is.EqualTo(["Home"]));
    }

    [Test]
    public void TheHomeItemIsCurrentOnlyAtTheRoot() {
        Assert.Multiple(() => {
            Assert.That(Current("/", "Home"), Is.True);
            Assert.That(Current("/blog", "Home"), Is.False);
            Assert.That(Current("/admin", "Home"), Is.False);
        });
    }

    [Test]
    public void TheAdminItemIsCurrentAtOrUnderItsPrefix() {
        Assert.Multiple(() => {
            Assert.That(Current("/admin", "Admin"), Is.True);
            Assert.That(Current("/admin/import", "Admin"), Is.True);
        });
    }

    [Test]
    public void TheFooterCarriesAboutAndThePrivacyPolicy() {
        Assert.That(SiteNavigation.FooterLinks().Select(link => (link.Href, link.Text)),
            Is.EqualTo(Footer));
    }

    [Test]
    public void TheMenuNamesItsLandmark() {
        string nav = SiteNavigation.Nav("/");

        MarkupReader document = MarkupReader.Parse(nav);

        Assert.That(document.Elements("nav").Single().Attribute("aria-label"), Is.EqualTo(SiteNavigation.Label));
    }

    private static string[] Texts(string path) =>
        [.. SiteNavigation.Links(path).Select(link => link.Text)];

    private static bool Current(string path, string text) =>
        SiteNavigation.Links(path)
            .Single(link => link.Text == text)
            .IsCurrent;
}
