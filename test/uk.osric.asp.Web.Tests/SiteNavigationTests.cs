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

    [Test]
    public void AnAnonymousVisitorIsOfferedHomeAboutPrivacyAndSignIn() {
        Assert.That(Texts(anonymous: true), Is.EqualTo(["Home", "About", "Privacy", "Sign in"]));
    }

    [Test]
    public void ASignedInVisitorIsOfferedEveryTool() {
        Assert.That(Texts(anonymous: false, assistant: true),
            Is.EqualTo(["Home", "About", "Privacy", "Assistant", "Pages", "Admin", "Sign out"]));
    }

    [Test]
    public void TheAssistantIsOfferedOnlyWhenItIsEnabled() {
        Assert.That(Texts(anonymous: false, assistant: false),
            Is.EqualTo(["Home", "About", "Privacy", "Pages", "Admin", "Sign out"]));
        Assert.That(Texts(anonymous: true, assistant: true),
            Is.EqualTo(["Home", "About", "Privacy", "Sign in"]));
    }

    [Test]
    public void TheSignInLinkCarriesTheCanonicalPathAndRawQuery() {
        NavLink signIn = SiteNavigation.Links(authenticated: false, assistantEnabled: false, "/blog", "?page=2", EditorPath)
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
    public void ThePrivacyItemIsCurrentOnlyAtPrivacy() {
        Assert.That(Current("/privacy", "Privacy"), Is.True);
        Assert.That(Current("/privacy/", "Privacy"), Is.False);
        Assert.That(Current("/", "Privacy"), Is.False);
    }

    [Test]
    public void TheAboutItemIsCurrentOnlyAtAbout() {
        Assert.That(Current("/about", "About"), Is.True);
        Assert.That(Current("/about/", "About"), Is.False);
        Assert.That(Current("/", "About"), Is.False);
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
        string nav = SiteNavigation.Nav(authenticated: true, assistantEnabled: true, "/", string.Empty, EditorPath);

        MarkupReader document = MarkupReader.Parse(nav);

        Assert.That(document.Elements("nav").Single().Attribute("aria-label"), Is.EqualTo(SiteNavigation.Label));
    }

    private static string[] Texts(bool anonymous, bool assistant = false) =>
        [.. SiteNavigation.Links(!anonymous, assistant, "/", string.Empty, EditorPath).Select(link => link.Text)];

    private static bool Current(string path, string text) =>
        SiteNavigation.Links(authenticated: true, assistantEnabled: true, path, string.Empty, EditorPath)
            .Single(link => link.Text == text)
            .IsCurrent;
}
