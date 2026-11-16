// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.WebUtilities;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;

/// <summary>The site menu the shell's own documents carry.</summary>
/// <remarks>
/// The seeded layout writes the same list for stored pages; an integration test compares the two so
/// the deliberate duplication cannot drift.
/// </remarks>
[TestFixture]
public sealed class SiteNavigationTests {

    // A key that is not the shipped default, so every assertion here proves the menu follows the
    // configured editor rather than a constant.
    private const string EditorPath = "/desk/edit";

    private static readonly (string Href, string Text)[] Footer = [("/about", "About"), ("/privacy", "Privacy policy")];

    [Test]
    public void AnAnonymousVisitorIsOfferedHomeAndSignIn() {
        Assert.That(Texts(anonymous: true), Is.EqualTo(["Home", "Sign in"]));
    }

    [Test]
    public void ASignedInVisitorIsOfferedEveryTool() {
        Assert.That(Texts(anonymous: false),
            Is.EqualTo(["Home", "Assistant", "Pages", "Admin", "Sign out"]));
    }

    [Test]
    public void AnAnonymousVisitorIsNeverOfferedTheAssistant() {
        Assert.That(Texts(anonymous: true), Does.Not.Contain("Assistant"));
    }

    [Test]
    public void TheSignInLinkCarriesTheCanonicalPathAndRawQuery() {
        NavLink signIn = SiteNavigation.Links(authenticated: false, "/blog", "?page=2", EditorPath)
            .Single(link => link.Text == "Sign in");

        string returnUrl = QueryHelpers.ParseQuery(new Uri("https://site.example" + signIn.Href).Query)["returnUrl"].ToString();

        Assert.That(signIn.Href, Does.StartWith("/login?returnUrl="));
        Assert.That(returnUrl, Is.EqualTo("/blog?page=2"));
    }

    [Test]
    public void TheHomeItemIsCurrentOnlyAtTheRoot() {
        Assert.That(Current("/", "Home"), Is.True);
        Assert.That(Current("/blog", "Home"), Is.False);
        Assert.That(Current("/pages", "Home"), Is.False);
    }

    [Test]
    public void TheFooterCarriesAboutAndThePrivacyPolicy() {
        Assert.That(SiteNavigation.FooterLinks().Select(link => (link.Href, link.Text)),
            Is.EqualTo(Footer));
    }

    [TestCase(EditorPath)]
    [TestCase(EditorPath + "/")]
    public void TheEditorPageMarksThePageListCurrent(string path) {
        Assert.That(Current(path, "Pages"), Is.True, path);
    }

    [TestCase("/admin")]
    [TestCase("/admin/import")]
    [TestCase("/admin/import/1")]
    [TestCase("/admin/export")]
    public void AnAdminRouteMarksAdminCurrent(string path) {
        Assert.That(Current(path, "Admin"), Is.True, path);
    }

    [Test]
    public void TheSignOutViewMarksSignOutCurrent() {
        Assert.That(Current("/logout", "Sign out"), Is.True);
        Assert.That(Current("/logout/", "Sign out"), Is.False);
    }

    [Test]
    public void TheMenuNamesItsLandmark() {
        string nav = SiteNavigation.Nav(authenticated: true, "/", string.Empty, EditorPath);

        MarkupReader document = MarkupReader.Parse(nav);

        Assert.That(document.Elements("nav").Single().Attribute("aria-label"), Is.EqualTo(SiteNavigation.Label));
    }

    private static string[] Texts(bool anonymous) =>
        [.. SiteNavigation.Links(!anonymous, "/", string.Empty, EditorPath).Select(link => link.Text)];

    private static bool Current(string path, string text) =>
        SiteNavigation.Links(authenticated: true, path, string.Empty, EditorPath)
            .Single(link => link.Text == text)
            .IsCurrent;
}
