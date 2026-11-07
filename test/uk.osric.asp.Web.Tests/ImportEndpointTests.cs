// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Text;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ImportEndpointTests {

    [Test]
    public async Task UploadingAnInvalidArchiveAnswers400AndStagesNothing() {
        ImportStageStore stages = NewStages();

        DefaultHttpContext context = await UploadAsync(stages, Encoding.UTF8.GetBytes("not a database"));

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("could not be imported"));
        Assert.That(stages.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task UploadingAnArchiveWithOneBadRowStagesNothing() {
        ImportStageStore stages = NewStages();
        byte[] archive = TestArchive.Build([("/about", "About"), ("/About", "Not canonical")]);

        DefaultHttpContext context = await UploadAsync(stages, archive);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("/About"));
        Assert.That(stages.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task UploadingAValidArchiveRedirectsToTheReview() {
        ImportStageStore stages = NewStages();
        byte[] archive = TestArchive.Build([("/about", "About")]);

        DefaultHttpContext context = await UploadAsync(stages, archive);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status303SeeOther));
        Assert.That(context.Response.Headers.Location.ToString(), Does.Match(@"^/admin/import/[0-9a-f]{32}$"));
        Assert.That(stages.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task TheReviewTagsANewPage() {
        ImportStageStore stages = NewStages();
        ImportStage stage = Stage(stages, "/about");

        string body = await ReviewAsync(stages, new FakePageStore(), stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "new" }));
    }

    [Test]
    public async Task TheReviewTagsAnOverwrite() {
        ImportStageStore stages = NewStages();
        FakePageStore pages = new() { ["/about"] = new PageTemplate(Key("/about"), "Old") };
        ImportStage stage = Stage(stages, "/about");

        string body = await ReviewAsync(stages, pages, stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "overwrite" }));
    }

    [Test]
    public async Task TheReviewTagsATombstonedPageAsRestore() {
        ImportStageStore stages = NewStages();
        FakePageStore pages = new() { ["/about"] = new PageTemplate(Key("/about"), "Old") };
        await pages.DeletePageAsync(Key("/about"));
        ImportStage stage = Stage(stages, "/about");

        string body = await ReviewAsync(stages, pages, stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "restore" }));
    }

    [Test]
    public async Task EveryPageIsTickedByDefault() {
        ImportStageStore stages = NewStages();
        ImportStage stage = stages.Create([
            new PageArchiveEntry(Key("/one"), "One", []),
            new PageArchiveEntry(Key("/two"), "Two", []),
        ]);

        string body = await ReviewAsync(stages, new FakePageStore(), stage.Id);

        Assert.That(Regex.Matches(body, "checked").Count, Is.EqualTo(2));
    }

    private static ImportStageStore NewStages() => new(new ArchiveOptions(), new TestClock());

    private static ImportStage Stage(ImportStageStore stages, params string[] paths) =>
        stages.Create([.. paths.Select(path => new PageArchiveEntry(Key(path), path, []))]);

    private static async Task<DefaultHttpContext> UploadAsync(ImportStageStore stages, byte[] archive) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/import");
        context.Request.ContentType = "multipart/form-data";
        FormFileCollection files = [new FormFile(new MemoryStream(archive), 0, archive.Length, "archive", "pages.sqlite")];
        Dictionary<string, StringValues> noFields = new(StringComparer.Ordinal);
        context.Request.Form = new FormCollection(noFields, files);
        ArchiveOptions options = new();

        await ImportEndpoint.UploadAsync(
            context,
            new PageArchiveReader(options, new PageStorageOptions(), new ReservedPaths(ReservedPaths.Default)),
            stages,
            options);

        return context;
    }

    private static async Task<string> ReviewAsync(ImportStageStore stages, FakePageStore pages, string id) {
        DefaultHttpContext context = ReviewContext(id);
        await ImportEndpoint.ReviewAsync(context, stages, pages, new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static DefaultHttpContext ReviewContext(string id) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/import/" + id);
        context.Request.RouteValues["id"] = id;
        return context;
    }

    private static List<string> Tags(string body) =>
        [.. Regex.Matches(body, "<span class=\"tag\">([^<]+)</span>").Select(match => match.Groups[1].Value)];

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
