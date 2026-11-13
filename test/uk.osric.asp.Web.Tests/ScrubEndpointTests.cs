// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ScrubEndpointTests {

    [Test]
    public async Task TheConfirmationNamesThePageAndItsCostAsync() {
        FakePageStore storage = new();
        PagePath path = await DeletedPageAsync(storage);
        using PageHarness pages = new(storage);

        string body = await ConfirmAsync(pages, path);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain($"Type {path.Key} to confirm"));
            Assert.That(body, Does.Contain("cannot be undone"));
            Assert.That(body, Does.Contain($"action=\"{path.Key.Replace("/", "/admin/scrub/", StringComparison.Ordinal)}\""),
                "the form posts to the shell's scrub route");
        });
    }

    [Test]
    public async Task AKeyThatIsNotAStoredPageIsNotFoundAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        DefaultHttpContext context = await ConfirmContextAsync(pages, "/missing");

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ALivePageIsRefusedWithTheDeleteThatHasToComeFirstAsync() {
        FakePageStore storage = new();
        storage["/live"] = new PageTemplate(PagePath.Normalize("/live"), "<p>live</p>");
        using PageHarness pages = new(storage);

        DefaultHttpContext context = await ConfirmContextAsync(pages, "/live");
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(409));
            Assert.That(body, Does.Contain("Delete the page before scrubbing its history"));
            Assert.That(body, Does.Contain("cmd=delete"), "the refusal names the delete that must come first");
        });
    }

    [Test]
    public async Task AMismatchedConfirmationRemovesNothingAsync() {
        FakePageStore storage = new();
        PagePath path = await DeletedPageAsync(storage);
        using PageHarness pages = new(storage);

        DefaultHttpContext context = PostContext(pages, path, "not-the-key");
        await ScrubEndpoint.ScrubAsync(context, pages.Editor, NullLoggerFactory.Instance);
        string body = await TestHttp.ReadBodyAsync(context);
        bool stillThere = await PageExistsAsync(storage, path);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(400));
            Assert.That(body, Does.Contain("Scrub was not confirmed"));
            Assert.That(stillThere, Is.True, "the history is still there");
        });
    }

    [Test]
    public async Task AMissingConfirmationIsRefusedAsync() {
        FakePageStore storage = new();
        PagePath path = await DeletedPageAsync(storage);
        using PageHarness pages = new(storage);

        DefaultHttpContext context = TestHttp.CreateContext("gone", "POST", "/admin/scrub/gone");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        await ScrubEndpoint.ScrubAsync(context, pages.Editor, NullLoggerFactory.Instance);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task TheTypedKeyScrubsThePageAndReturnsToTheEditorPageAsync() {
        FakePageStore storage = new();
        PagePath path = await DeletedPageAsync(storage);
        using PageHarness pages = new(storage);

        DefaultHttpContext context = PostContext(pages, path, path.Key);
        await ScrubEndpoint.ScrubAsync(context, pages.Editor, NullLoggerFactory.Instance);
        bool stillThere = await PageExistsAsync(storage, path);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(303));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/pages"));
            Assert.That(stillThere, Is.False, "the page and its history are gone");
        });
    }

    [Test]
    public async Task ScrubbingALivePageByPostIsRefusedAsync() {
        FakePageStore storage = new();
        storage["/live"] = new PageTemplate(PagePath.Normalize("/live"), "<p>live</p>");
        using PageHarness pages = new(storage);

        DefaultHttpContext context = PostContext(pages, PagePath.Normalize("/live"), "/live");
        await ScrubEndpoint.ScrubAsync(context, pages.Editor, NullLoggerFactory.Instance);
        bool stillThere = await PageExistsAsync(storage, PagePath.Normalize("/live"));

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(409));
            Assert.That(stillThere, Is.True);
        });
    }

    // A tombstone has no live page, so the head is what says the row is still there.
    private static async Task<bool> PageExistsAsync(FakePageStore storage, PagePath path) =>
        await storage.GetHeadAsync(path) is not null;

    private static async Task<PagePath> DeletedPageAsync(FakePageStore storage) {
        PagePath path = PagePath.Normalize("/gone");
        storage[path.Key] = new PageTemplate(path, "<p>gone</p>");
        await storage.DeletePageAsync(path);
        return path;
    }

    private static async Task<string> ConfirmAsync(PageHarness pages, PagePath path) {
        DefaultHttpContext context = await ConfirmContextAsync(pages, path.Key);
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<DefaultHttpContext> ConfirmContextAsync(PageHarness pages, string path) {
        // The route value is the key without its leading slash, which is what the catch-all binds.
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", "/admin/scrub" + path);
        await ScrubEndpoint.ConfirmAsync(context, pages.Editor, new TestAntiforgery());
        return context;
    }

    private static DefaultHttpContext PostContext(PageHarness pages, PagePath path, string confirmation) {
        DefaultHttpContext context = TestHttp.CreateContext(path.Key.TrimStart('/'), "POST", "/admin/scrub" + path.Key);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues>(
            StringComparer.Ordinal) {
            ["confirmation"] = confirmation,
        });
        return context;
    }
}
