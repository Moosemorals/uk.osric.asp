// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Text;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ImportEndpointTests {

    private const string BrokenSource = "<% int value = ; %>";

    [Test]
    public async Task UploadingAnInvalidArchiveAnswers400AndStagesNothing() {
        using ImportHarness harness = new();

        DefaultHttpContext context = await UploadAsync(harness, Encoding.UTF8.GetBytes("not a database"));

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("could not be imported"));
        Assert.That(harness.Stages.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task UploadingAnArchiveWithOneBadRowStagesNothing() {
        using ImportHarness harness = new();
        byte[] archive = TestArchive.Build([("/about", "About"), ("/About", "Not canonical")]);

        DefaultHttpContext context = await UploadAsync(harness, archive);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("/About"));
        Assert.That(harness.Stages.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task UploadingAValidArchiveRedirectsToTheReview() {
        using ImportHarness harness = new();
        byte[] archive = TestArchive.Build([("/about", "About")]);

        DefaultHttpContext context = await UploadAsync(harness, archive);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status303SeeOther));
        Assert.That(context.Response.Headers.Location.ToString(), Does.Match(@"^/admin/import/[0-9a-f]{32}$"));
        Assert.That(harness.Stages.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task TheUploadAndReviewFormsLoadTheAdminScriptAsAModuleAsync() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/about", "About", []));
        DefaultHttpContext upload = TestHttp.CreateContext(null, "GET", "/admin/import");

        await ImportEndpoint.UploadFormAsync(upload, new TestAntiforgery(), harness.Options);
        string uploadBody = await TestHttp.ReadBodyAsync(upload);
        string reviewBody = await ReviewAsync(harness, stage.Id);

        Assert.Multiple(() => {
            Assert.That(uploadBody, Does.Contain("<script type=\"module\" src=\"/admin.js\"></script>"));
            Assert.That(reviewBody, Does.Contain("<script type=\"module\" src=\"/admin.js\"></script>"));
        });
    }

    [Test]
    public async Task TheReviewTagsANewPage() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/about", "About", []));

        string body = await ReviewAsync(harness, stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "new" }));
    }

    [Test]
    public async Task TheReviewTagsAnOverwrite() {
        using ImportHarness harness = new();
        harness.Pages["/about"] = new PageTemplate(Key("/about"), "Old");
        ImportStage stage = harness.Stage(("/about", "About", []));

        string body = await ReviewAsync(harness, stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "overwrite" }));
    }

    [Test]
    public async Task TheReviewTagsATombstonedPageAsRestore() {
        using ImportHarness harness = new();
        harness.Pages["/about"] = new PageTemplate(Key("/about"), "Old");
        await harness.Pages.DeletePageAsync(Key("/about"));
        ImportStage stage = harness.Stage(("/about", "About", []));

        string body = await ReviewAsync(harness, stage.Id);

        Assert.That(Tags(body), Is.EqualTo(new List<string> { "restore" }));
    }

    [Test]
    public async Task EveryPageIsTickedByDefault() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/one", "One", []), ("/two", "Two", []));

        string body = await ReviewAsync(harness, stage.Id);

        Assert.That(Regex.Matches(body, "checked").Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ApprovingCommitsOnlyTheTickedPages() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/one", "One", []), ("/two", "Two", []));

        DefaultHttpContext context = await ApproveAsync(harness, stage.Id, ["/one"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status303SeeOther));
        Assert.That(harness.Batches.Saved.Select(save => save.Template.Path.Key), Is.EqualTo(new List<string> { "/one" }));
    }

    [Test]
    public async Task AnOverwriteAddsARevisionAndKeepsTheOldOne() {
        using ImportHarness harness = new();
        harness.Pages["/about"] = new PageTemplate(Key("/about"), "Old");
        string oldRevisionId = harness.Pages["/about"]!.RevisionId;
        ImportStage stage = harness.Stage(("/about", "About", []));

        await ApproveAsync(harness, stage.Id, ["/about"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        PageTemplate saved = harness.Batches.Saved.Single().Template;
        Assert.That(saved.RevisionId, Is.Not.EqualTo(oldRevisionId));
        Assert.That(saved.Template, Is.EqualTo("About"));
        Assert.That(harness.Pages["/about"]!.RevisionId, Is.EqualTo(oldRevisionId), "the batch store is what replaces the head");
    }

    [Test]
    public async Task StateIsCommittedWithThePage() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/about", "About", [new("visits", "3")]));

        await ApproveAsync(harness, stage.Id, ["/about"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        Assert.That(harness.Batches.Saved.Single().State, Is.EqualTo(new List<PageStateValue> { new("visits", "3") }));
    }

    [Test]
    public async Task APageThatDoesNotCompileIsCommittedWithTheRest() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/broken", BrokenSource, []), ("/fine", "Fine", []));

        await ApproveAsync(harness, stage.Id, ["/broken", "/fine"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        Assert.That(harness.Batches.Saved.Select(save => save.Template.Path.Key),
            Is.EqualTo(new List<string> { "/broken", "/fine" }));
        PageTemplate broken = harness.Batches.Saved[0].Template;
        Assert.That(broken.Image, Is.Not.Null);
        Assert.That(broken.Image!.Bytes, Is.Null);
        Assert.That(broken.Image.Diagnostics, Is.Not.Empty);
        Assert.That(stage.Snapshot().Status, Is.EqualTo(ImportBatchStatus.Committed));
    }

    [Test]
    public async Task ACompilerFaultCommitsTheSourceWithoutAnImage() {
        using ImportHarness harness = new(compiler: new ThrowingCompiler());
        ImportStage stage = harness.Stage(("/about", "About", []));

        await ApproveAsync(harness, stage.Id, ["/about"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        PageTemplate saved = harness.Batches.Saved.Single().Template;
        Assert.That(saved.Template, Is.EqualTo("About"));
        Assert.That(saved.Image, Is.Null);
        Assert.That(stage.Snapshot().Results.Single().Status, Is.EqualTo(ImportPageStatus.CompilerFault));
    }

    [Test]
    public async Task ACommitFailureLeavesEverySelectedPageUntouched() {
        using ImportHarness harness = new();
        harness.Batches.Failure = new IOException("the disk is full");
        ImportStage stage = harness.Stage(("/one", "One", []), ("/two", "Two", []));

        await ApproveAsync(harness, stage.Id, ["/one", "/two"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        Assert.That(harness.Batches.Saved, Is.Empty);
        Assert.That(stage.Snapshot().Status, Is.EqualTo(ImportBatchStatus.Failed));
        Assert.That(stage.Snapshot().Message, Does.Contain("none of the selected pages changed"));
    }

    [Test]
    public async Task AMergedStateOverTheCapRefusesTheWholeBatch() {
        using ImportHarness harness = new();
        harness.Batches.Refuse = [Key("/two")];
        ImportStage stage = harness.Stage(("/one", "One", []), ("/two", "Two", []));

        await ApproveAsync(harness, stage.Id, ["/one", "/two"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        ImportStageSnapshot snapshot = stage.Snapshot();
        Assert.That(snapshot.Status, Is.EqualTo(ImportBatchStatus.Failed));
        Assert.That(snapshot.Refused.Select(path => path.Key), Is.EqualTo(new List<string> { "/two" }));
        Assert.That(harness.Batches.Saved, Is.Empty);
    }

    [Test]
    public async Task AnImportOverTheCompiledCapWritesNothing() {
        using ImportHarness harness = new(options: new ArchiveOptions { MaxCompiledBytes = 1 });
        ImportStage stage = harness.Stage(("/about", "About <% ctx.Write(\"hello\"); %>", []));

        await ApproveAsync(harness, stage.Id, ["/about"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);

        Assert.That(harness.Batches.CallCount, Is.EqualTo(0));
        Assert.That(stage.Snapshot().Status, Is.EqualTo(ImportBatchStatus.Failed));
    }

    [Test]
    public async Task ASecondApprovalWhileRunningIsRefused() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/about", "About", []));

        await ApproveAsync(harness, stage.Id, ["/about"]);
        DefaultHttpContext second = await ApproveAsync(harness, stage.Id, ["/about"]);

        Assert.That(second.Response.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
        Assert.That(await TestHttp.ReadBodyAsync(second), Does.Contain("already running"));
    }

    [Test]
    public async Task AnExpiredStageIsNotFound() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/about", "About", []));
        harness.Clock.Now = stage.CreatedAt + TimeSpan.FromMinutes(harness.Options.StageTtlMinutes + 1);

        DefaultHttpContext context = ProgressContext(stage.Id);
        await ImportEndpoint.ReviewAsync(context, harness.Stages, harness.Pages, new TestAntiforgery());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Upload the archive again"));
    }

    [Test]
    public async Task TheProgressPageReportsEachOutcome() {
        using ImportHarness harness = new();
        ImportStage stage = harness.Stage(("/broken", BrokenSource, []), ("/fine", "Fine", []));

        await ApproveAsync(harness, stage.Id, ["/broken", "/fine"]);
        await harness.Processor.ProcessAsync(stage, CancellationToken.None);
        string body = await ProgressAsync(harness, stage.Id);

        Assert.That(body, Does.Contain("saved"));
        Assert.That(body, Does.Contain("compiled with errors"));
        Assert.That(body, Does.Contain(">compiled<"));
        Assert.That(body, Does.Not.Contain("http-equiv=\"refresh\""));
    }

    private static async Task<DefaultHttpContext> UploadAsync(ImportHarness harness, byte[] archive) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/import");
        context.Request.ContentType = "multipart/form-data";
        FormFileCollection files = [new FormFile(new MemoryStream(archive), 0, archive.Length, "archive", "pages.sqlite")];
        Dictionary<string, StringValues> noFields = new(StringComparer.Ordinal);
        context.Request.Form = new FormCollection(noFields, files);

        await ImportEndpoint.UploadAsync(context, harness.Reader, harness.Stages, harness.Options);

        return context;
    }

    private static async Task<string> ReviewAsync(ImportHarness harness, string id) {
        DefaultHttpContext context = ReviewContext(id);
        await ImportEndpoint.ReviewAsync(context, harness.Stages, harness.Pages, new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<DefaultHttpContext> ApproveAsync(ImportHarness harness, string id, string[] included) {
        DefaultHttpContext context = ReviewContext(id);
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["include"] = included,
        });

        await ImportEndpoint.ApproveAsync(context, harness.Stages, harness.Queue, harness.Reserved);

        return context;
    }

    private static async Task<string> ProgressAsync(ImportHarness harness, string id) {
        DefaultHttpContext context = ReviewContext(id);
        context.Request.Path = "/admin/import/" + id + "/progress";
        await ImportEndpoint.ProgressAsync(context, harness.Stages);
        return await TestHttp.ReadBodyAsync(context);
    }

    private static DefaultHttpContext ProgressContext(string id) {
        DefaultHttpContext context = ReviewContext(id);
        context.Request.Path = "/admin/import/" + id + "/progress";
        return context;
    }

    private static DefaultHttpContext ReviewContext(string id) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/import/" + id);
        context.Request.RouteValues["id"] = id;
        return context;
    }

    private static List<string> Tags(string body) =>
        [.. Regex.Matches(body, "<span class=\"tag\">([^<]+)</span>").Select(match => match.Groups[1].Value)];

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private sealed class ThrowingCompiler : IPageCompiler {
        public string RuntimeId => "test-runtime";

        public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the fixture failed this compile");

        public LoadedPage Load(byte[] image, byte[]? symbols = null) =>
            throw new NotSupportedException();
    }
}
