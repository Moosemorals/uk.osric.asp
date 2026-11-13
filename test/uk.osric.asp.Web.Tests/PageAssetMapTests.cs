// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAssetMapTests {

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Test]
    public void ABareLiveAssetLinkIsVersioned() {
        IReadOnlyDictionary<string, string> map = new Dictionary<string, string> {
            ["/site.css"] = Hash,
        };

        Assert.That(PageAssetMap.Resolve(map, "/site.css"),
            Is.EqualTo($"/site.css?v={Hash}"));
    }

    [TestCase("/site.css?x=1")]
    [TestCase("/site.css#top")]
    [TestCase("https://example.com/site.css")]
    [TestCase("//example.com/site.css")]
    [TestCase("site.css")]
    [TestCase("/Site.css")]
    [TestCase("/missing.css")]
    public void AnythingThatIsNotABareKnownKeyIsUnchanged(string href) {
        IReadOnlyDictionary<string, string> map = new Dictionary<string, string> {
            ["/site.css"] = Hash,
        };

        Assert.That(PageAssetMap.Resolve(map, href), Is.EqualTo(href));
    }

    [Test]
    public async Task TheMapReadsOnlyLiveNoCodeHeadsAsync() {
        FakePageReadStore store = new();
        store.Save("/site.css", "body { }", templateHasCode: false);
        store.Save("/rocket.svg", "<svg />", templateHasCode: false);
        store.Save("/page.html", "<!DOCTYPE html>", templateHasCode: false);
        store.Save("/notes", "<h1>Fragment</h1>", templateHasCode: false);
        store.Save("/app.js", "export {};", templateHasCode: true);
        store.Save("/gone.css", "body { }", templateHasCode: false);
        store.Delete("/gone.css");

        IReadOnlyDictionary<string, string> map = await PageAssetMap.LoadAsync(store, 500);

        Assert.That(map.Keys, Is.EquivalentTo(["/site.css", "/rocket.svg", "/page.html", "/notes"]));
    }
}
