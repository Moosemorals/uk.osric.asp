// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ExportEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);

    private FakePageStateStore _state = new();

    [SetUp]
    public void NewState() => _state = new FakePageStateStore();

    [Test]
    public async Task TheListShowsEveryReachableLivePageTickedAsync() {
        FakePageStore store = NewStore();

        string body = await ListAsync(store);

        Assert.That(Checkboxes(body), Is.EqualTo(new List<string> { "/", "/about", "/gone" }));
        Assert.That(body, Does.Contain("checked"));
        Assert.That(body, Does.Contain("Page administration"));
    }

    [Test]
    public async Task TheListLoadsItsScriptAsAModuleAsync() {
        string body = await ListAsync(NewStore());

        Assert.That(body, Does.Contain("<script type=\"module\" src=\"/admin.js\"></script>"));
    }

    [Test]
    public async Task TheListSkipsDeletedReservedAndUnaddressablePagesAsync() {
        FakePageStore store = NewStore();
        store["/edit/hidden"] = new PageTemplate(Key("/edit/hidden"), "Reserved");
        store["/odd?key"] = new PageTemplate(Key("/odd?key"), "Unaddressable");
        await store.DeletePageAsync(Key("/gone"));

        string body = await ListAsync(store);

        Assert.That(Checkboxes(body), Is.EqualTo(new List<string> { "/", "/about" }));
    }

    [Test]
    public async Task DownloadWritesThePagesAndStateTablesAsync() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, ["/about", "/"]);

        PageArchiveReadResult result = Reader().Read(Body(context));
        Assert.That(result.Problems, Is.Empty);
        Assert.That(result.Pages.Select(page => page.Path.Key), Is.EqualTo(new List<string> { "/", "/about" }));
        Assert.That(result.Pages[1].Source, Is.EqualTo("About page"));
    }

    [Test]
    public async Task DownloadIncludesStateAsync() {
        FakePageStore store = NewStore();
        _state.Seed("/about", "visits", "3");

        DefaultHttpContext context = await DownloadAsync(store, ["/about"]);

        PageArchiveReadResult result = Reader().Read(Body(context));
        Assert.That(result.Pages.Single().State, Is.EqualTo(new List<PageStateValue> { new("visits", "3") }));
    }

    [Test]
    public async Task DownloadSkipsUntickedPagesAsync() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, ["/about"]);

        Assert.That(Reader().Read(Body(context)).Pages.Select(page => page.Path.Key),
            Is.EqualTo(new List<string> { "/about" }));
    }

    [Test]
    public async Task DownloadRefusesAKeyThatIsNotALivePageAsync() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, ["/ghost"]);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("no longer available"));
    }

    [Test]
    public async Task DownloadSetsTheSqliteContentTypeAndAnIsoTimestampAsync() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, ["/about"]);

        Assert.That(context.Response.ContentType, Is.EqualTo("application/vnd.sqlite3"));
        string disposition = context.Response.Headers.ContentDisposition.ToString();
        Assert.That(disposition, Does.StartWith("attachment; filename=\"pages-export-"));
        Assert.That(disposition, Does.Match(@"pages-export-\d{8}T\d{6}Z\.sqlite""$"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task DownloadSetsAContentLengthAsync() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, ["/about"]);

        Assert.That(context.Response.ContentLength, Is.EqualTo(Body(context).Length));
    }

    [Test]
    public async Task AnEmptySelectionAnswers400Async() {
        FakePageStore store = NewStore();

        DefaultHttpContext context = await DownloadAsync(store, []);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("No pages were selected"));
    }

    private static async Task<string> ListAsync(FakePageStore store) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/export");

        await ExportEndpoint.ListAsync(context, store, Reserved, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private async Task<DefaultHttpContext> DownloadAsync(FakePageStore store, string[] included) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/export");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["include"] = included,
        });

        await ExportEndpoint.DownloadAsync(context, store, _state, Reserved, NullLoggerFactory.Instance);

        return context;
    }

    private static byte[] Body(DefaultHttpContext context) {
        Assert.That(context.Response.Body, Is.TypeOf<MemoryStream>());
        return ((MemoryStream)context.Response.Body).ToArray();
    }

    private static List<string> Checkboxes(string body) {
        List<string> values = [];
        int index = 0;

        while ((index = body.IndexOf("name=\"include\" value=\"", index, StringComparison.Ordinal)) >= 0) {
            index += "name=\"include\" value=\"".Length;
            int end = body.IndexOf('"', index);
            values.Add(Uri.UnescapeDataString(body[index..end]));
            index = end;
        }

        values.Sort(StringComparer.Ordinal);
        return values;
    }

    private static FakePageStore NewStore() {
        FakePageStore store = new() {
            ["/about"] = new PageTemplate(Key("/about"), "About page"),
            ["/"] = new PageTemplate(Key("/"), "Home page"),
            ["/gone"] = new PageTemplate(Key("/gone"), "Deleted page"),
        };

        return store;
    }

    private static PageArchiveReader Reader() =>
        new(new ArchiveOptions(), new PageStorageOptions(), Reserved, NullLogger<PageArchiveReader>.Instance);

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
