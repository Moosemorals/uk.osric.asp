// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;

[TestFixture]
public sealed class ReservedPathsTests {

    private static readonly ReservedPaths Defaults = new(ReservedPaths.Default);

    [TestCase("/admin/scrub")]
    [TestCase("/admin/scrub/page")]
    [TestCase("/admin/scrub/a/b")]
    [TestCase("/favicon.ico")]
    [TestCase("/health")]
    [TestCase("/login")]
    [TestCase("/logout")]
    [TestCase("/metrics")]
    [TestCase("/admin/status")]
    [TestCase("/admin/status/chart")]
    [TestCase("/admin.css")]
    [TestCase("/admin.js")]
    [TestCase("/reference.css")]
    [TestCase("/rocket.svg")]
    [TestCase("/status.css")]
    [TestCase("/assistant")]
    [TestCase("/assistant/1")]
    [TestCase("/admin/import")]
    [TestCase("/admin/import/1")]
    [TestCase("/admin/export")]
    public void DefaultKeysAreReserved(string path) {
        Assert.That(Defaults.IsReserved(PagePath.Normalize(path)), Is.True);
    }

    [TestCase("/")]
    [TestCase("/edits")]
    [TestCase("/editing")]
    [TestCase("/site.css")]
    [TestCase("/site.css.bak")]
    [TestCase("/hello")]
    [TestCase("/administrator")]
    [TestCase("/adminx")]
    public void SimilarKeysAreNotReserved(string path) {
        Assert.That(Defaults.IsReserved(PagePath.Normalize(path)), Is.False);
    }

    [TestCase("/edit")]
    [TestCase("/edit/page")]
    [TestCase("/pages")]
    public void TheRetiredEditorKeyIsAnOrdinaryPageKey(string path) {
        Assert.That(Defaults.IsReserved(PagePath.Normalize(path)), Is.False);
    }

    // An ActivityPub node has to point a peer at this key, and nothing claims it: it is an ordinary
    // page key the site may serve, so the proof is a test rather than a route.
    [Test]
    public void AWebfingerKeyIsAnOrdinaryPageKey() {
        PagePath key = PagePath.Normalize("/.well-known/webfinger");

        Assert.Multiple(() => {
            Assert.That(key.IsAddressable, Is.True);
            Assert.That(Defaults.IsReserved(key), Is.False);
            Assert.That(ShellCatalogue.Routes.Select(route => route.Key), Has.No.Member(key.Key),
                "no shell route claims it");
        });
    }

    [Test]
    public void TheAdminIndexIsReservedWithItsTools() {
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/admin")), Is.True);
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/admin/import")), Is.True);
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/admin/export")), Is.True);
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/admin/scrub")), Is.True);
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/administrator")), Is.False);
    }

    [Test]
    public void KeysAreComparedCaseInsensitively() {
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/ADMIN/SCRUB")), Is.True);
    }

    [Test]
    public void AnEmptyListIsRefusedRatherThanReservingNothing() {
        Assert.That(() => new ReservedPaths([]), Throws.ArgumentException);
    }

    [Test]
    public void ConfiguredKeysReplaceTheDefaults() {
        ReservedPaths reserved = new(["/admin"]);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/admin/page")), Is.True);
        Assert.That(reserved.IsReserved(PagePath.Normalize("/login")), Is.False);
    }

    [TestCase("/Admin/")]
    [TestCase("admin")]
    public void ConfiguredKeysAreNormalised(string path) {
        ReservedPaths reserved = new([path]);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/admin")), Is.True);
    }
}
