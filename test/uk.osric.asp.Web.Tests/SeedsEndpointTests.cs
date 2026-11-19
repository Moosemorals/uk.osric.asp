// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SeedsEndpointTests {

    [Test]
    public async Task ThePreviewTicksTheSafeRowsAndLeavesTheDriftedRowsUntickedAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile editorCss = TestOptions.Seeds.Find(Key("/editor.css"))!;
        SeedFile help = TestOptions.Seeds.Find(Key("/help"))!;

        storage["/about"] = new PageTemplate(Key("/about"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        storage["/editor.css"] = new PageTemplate(Key("/editor.css"), editorCss.Source) { SeedHash = TemplateFingerprint.Of("other") };
        storage["/help"] = new PageTemplate(Key("/help"), "mine") { SeedHash = help.Hash };
        storage["/site.css"] = new PageTemplate(Key("/site.css"), "mine") { SeedHash = TemplateFingerprint.Of("other") };

        using PageHarness pages = new(storage);
        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(Row(body, "/about"), Does.Contain("checked"));
            Assert.That(Row(body, "/privacy"), Does.Contain("checked"));
            Assert.That(Row(body, "/editor.css"), Does.Contain("checked"), "an adoptable row is safe");
            Assert.That(Row(body, "/help"), Does.Not.Contain("checked"));
            Assert.That(Row(body, "/site.css"), Does.Not.Contain("checked"));
            Assert.That(Row(body, "/about"), Does.Contain("Write the newer seed"));
            Assert.That(Row(body, "/editor.css"), Does.Contain("Record it as installed"));
            Assert.That(Row(body, "/help"), Does.Contain("Replace it with the bundled seed"));
        });
    }

    [Test]
    public async Task ThePreviewOffersNoFormWhenEverySeedIsInSyncAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        using PageHarness pages = new(storage);

        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("<form"));
            Assert.That(body, Does.Contain("nothing to apply"));
            Assert.That(body, Does.Contain("No stored page is at a key the shell answers"));
        });
    }

    [Test]
    public async Task ApplyingWritesALaterRowPastAnUntickedDriftAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        SeedFile privacy = TestOptions.Seeds.Find(Key("/privacy"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "mine") { SeedHash = about.Hash };
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "old") { SeedHash = TemplateFingerprint.Of("old") };

        using PageHarness pages = new(storage);
        string body = await PostAsync(pages, await SeedFormAsync(storage, "/privacy"));

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo("mine"), "the unticked edited row is untouched");
            Assert.That(storage["/privacy"]?.Template, Is.EqualTo(privacy.Source));
            Assert.That(body, Does.Contain("1 written"));
            Assert.That(body, Does.Contain("not ticked"));
        });
    }

    [Test]
    public async Task ATickedEditedRowIsReplacedAndKeepsItsOldSourceInHistoryAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile help = TestOptions.Seeds.Find(Key("/help"))!;
        storage["/help"] = new PageTemplate(Key("/help"), "mine") { SeedHash = help.Hash };
        PageTemplate old = storage["/help"]!;

        using PageHarness pages = new(storage);
        string body = await PostAsync(pages, await SeedFormAsync(storage, "/help"));
        PageTemplate? previous = await storage.GetRevisionByHashAsync(old.Hash);

        Assert.Multiple(() => {
            Assert.That(storage["/help"]?.Template, Is.EqualTo(help.Source));
            Assert.That(storage["/help"]?.SeedHash, Is.EqualTo(help.Hash));
            Assert.That(previous?.Template, Is.EqualTo("mine"), "the source it replaced is still a revision");
            Assert.That(body, Does.Contain("written"));
        });
    }

    [Test]
    public async Task ATickedStoreNewerRowIsRecordedAndWritesNoRevisionAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile editorCss = TestOptions.Seeds.Find(Key("/editor.css"))!;
        storage["/editor.css"] = new PageTemplate(Key("/editor.css"), editorCss.Source) { SeedHash = TemplateFingerprint.Of("other") };
        int writes = storage.WriteCount;

        using PageHarness pages = new(storage);
        string body = await PostAsync(pages, await SeedFormAsync(storage, "/editor.css"));
        PageHeadSummary? head = await storage.GetHeadAsync(Key("/editor.css"));

        Assert.Multiple(() => {
            Assert.That(head?.SeedHash, Is.EqualTo(editorCss.Hash));
            Assert.That(storage.WriteCount, Is.EqualTo(writes), "an adopt writes no revision");
            Assert.That(body, Does.Contain("recorded as installed"));
        });
    }

    [Test]
    public async Task ATickedNewSeedIsCreatedAndATickedTombstoneComesBackAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage, except: "/about");
        using PageHarness pages = new(storage);

        string created = await PostAsync(pages, await SeedFormAsync(storage, "/about"));
        Assert.That(storage["/about"]?.Template, Is.EqualTo(TestOptions.SeedSource(Key("/about"))));
        Assert.That(created, Does.Contain("written"));

        await pages.Editor.DeleteAsync(Key("/privacy"));
        string restored = await PostAsync(pages, await SeedFormAsync(storage, "/privacy"));
        PageHeadSummary? head = await storage.GetHeadAsync(Key("/privacy"));

        Assert.Multiple(() => {
            Assert.That(storage["/privacy"]?.Template, Is.EqualTo(TestOptions.SeedSource(Key("/privacy"))));
            Assert.That(head?.IsDeleted, Is.False);
            Assert.That(restored, Does.Contain("written"));
        });
    }

    [Test]
    public async Task ARowThatMovedIsRefusedAndTheOtherTickStillRunsAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        SeedFile privacy = TestOptions.Seeds.Find(Key("/privacy"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "old") { SeedHash = TemplateFingerprint.Of("old") };

        Dictionary<string, StringValues> form = await SeedFormAsync(storage, "/about", "/privacy");
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "changed after preview") { SeedHash = privacy.Hash };

        using PageHarness pages = new(storage);
        string body = await PostAsync(pages, form);

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo(about.Source));
            Assert.That(storage["/privacy"]?.Template, Is.EqualTo("changed after preview"));
            Assert.That(body, Does.Contain("1 written"));
            Assert.That(body, Does.Contain("changed since the page loaded"));
        });
    }

    [Test]
    public async Task NothingTickedWritesNothingAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        using PageHarness pages = new(storage);
        int writes = storage.WriteCount;

        string body = await PostAsync(pages, new Dictionary<string, StringValues> {
            ["action"] = "apply",
        });

        Assert.Multiple(() => {
            Assert.That(storage.WriteCount, Is.EqualTo(writes));
            Assert.That(body, Does.Contain("Nothing was ticked"));
        });
    }

    [Test]
    public async Task AnIndexThePreviewDidNotOfferIsRefusedAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        using PageHarness pages = new(storage);

        DefaultHttpContext context = await PostContextAsync(pages, new Dictionary<string, StringValues> {
            ["action"] = "apply",
            ["seed"] = "999",
        });

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(storage.WriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task AFailedWriteDoesNotStopTheOthersAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        SeedFile privacy = TestOptions.Seeds.Find(Key("/privacy"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        storage["/privacy"] = new PageTemplate(Key("/privacy"), "old") { SeedHash = TemplateFingerprint.Of("old") };
        Dictionary<string, StringValues> form = await SeedFormAsync(storage, "/about", "/privacy");

        FakePageRevisionStore revisions = new(storage) {
            OnSave = revision => {
                if (revision.Path == Key("/about")) {
                    throw new InvalidOperationException("the disk is full");
                }
            },
        };

        using PageHarness pages = new(storage, revisionStore: revisions);
        string body = await PostAsync(pages, form);

        Assert.Multiple(() => {
            Assert.That(storage["/about"]?.Template, Is.EqualTo("old"));
            Assert.That(storage["/privacy"]?.Template, Is.EqualTo(privacy.Source));
            Assert.That(body, Does.Contain("1 written"));
            Assert.That(body, Does.Contain("1 failed"));
        });
    }

    [Test]
    public async Task AFormThatFailedVerificationWritesNothingAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        using PageHarness pages = new(storage);

        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", SeedsEndpoint.Path);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["action"] = "apply",
        });
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));

        await SeedsEndpoint.PostAsync(context, pages.Editor, TestOptions.Seeds, pages.Storage,
            TestOptions.Reserved, NullLoggerFactory.Instance);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(storage.WriteCount, Is.Zero);
        });
    }

    [Test]
    public async Task TheDiffShowsTheStoredSourceAgainstTheSeedAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "mine") { SeedHash = about.Hash };

        using PageHarness pages = new(storage);
        string body = await PreviewAsync(pages, "?diff=" + Uri.EscapeDataString("/about"));

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("What writing the seed of /about would change"));
            Assert.That(body, Does.Contain("mine"));
            AssertBare(body);
        });
    }

    [Test]
    public async Task ThePageLoadsNothingTheOwnerCanBreakAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        SeedFile about = TestOptions.Seeds.Find(Key("/about"))!;
        storage["/about"] = new PageTemplate(Key("/about"), "mine") { SeedHash = about.Hash };
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");

        using PageHarness pages = new(storage);
        string preview = await PreviewAsync(pages);
        string result = await PostAsync(pages, await SeedFormAsync(storage, "/about"));
        string diff = await PreviewAsync(pages, "?diff=/about");
        DefaultHttpContext refused = await PostContextAsync(pages, new Dictionary<string, StringValues> {
            ["action"] = "unknown",
        });
        string refusal = await TestHttp.ReadBodyAsync(refused);

        foreach (string body in new[] { preview, result, diff, refusal }) {
            AssertBare(body);
        }
    }

    [Test]
    public async Task ThePreviewListsALiveStrayAndATombstoneAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");
        storage["/status.css"] = new PageTemplate(Key("/status.css"), "stray");
        await storage.DeletePageAsync(Key("/status.css"));
        storage["/mine"] = new PageTemplate(Key("/mine"), "mine");

        using PageHarness pages = new(storage);
        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(StrayRow(body, "/admin.css"), Does.Contain("live"));
            Assert.That(StrayRow(body, "/status.css"), Does.Contain("tombstone"));
            Assert.That(body, Does.Not.Contain("<th scope=\"row\">/mine</th>"));
            Assert.That(body, Does.Contain("name=\"stray\""));
        });
    }

    [Test]
    public async Task ThePreviewOffersNoRepairFormWhenThereAreNoStraysAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        using PageHarness pages = new(storage);

        string body = await PreviewAsync(pages);

        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("name=\"stray\""));
            Assert.That(body, Does.Contain("No stored page is at a key the shell answers"));
        });
    }

    [Test]
    public async Task AConfirmationThatIsNotTheWordRemovesNothingAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");
        Dictionary<string, StringValues> form = ScrubForm("/admin.css");
        form["confirm"] = "nope";

        using PageHarness pages = new(storage);
        DefaultHttpContext context = await PostContextAsync(pages, form);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(storage["/admin.css"]?.Template, Is.EqualTo("stray"));
        });
    }

    [Test]
    public async Task NothingTickedInTheRepairRemovesNothingAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");
        using PageHarness pages = new(storage);

        string body = await PostAsync(pages, new Dictionary<string, StringValues> {
            ["action"] = "scrub",
            ["confirm"] = "scrub",
        });

        Assert.Multiple(() => {
            Assert.That(storage["/admin.css"]?.Template, Is.EqualTo("stray"));
            Assert.That(body, Does.Contain("Nothing was ticked"));
        });
    }

    [Test]
    public async Task ANormalPageIsNeverScrubbedByTheRepairAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");
        storage["/mine"] = new PageTemplate(Key("/mine"), "mine");

        using PageHarness pages = new(storage);
        DefaultHttpContext context = await PostContextAsync(pages, ScrubForm("/admin.css", "/mine"));

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(storage["/admin.css"]?.Template, Is.EqualTo("stray"), "the batch is all or nothing");
            Assert.That(storage["/mine"]?.Template, Is.EqualTo("mine"));
        });
    }

    [Test]
    public async Task ASelectedStrayIsScrubbedAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        PagePath stray = Key("/admin.css");
        await storage.SavePageAsync(new PageTemplate(stray, "first"));
        await storage.SavePageAsync(new PageTemplate(stray, "second"));

        using PageHarness pages = new(storage);
        string body = await PostAsync(pages, ScrubForm("/admin.css"));
        PageHeadSummary? head = await storage.GetHeadAsync(stray);

        Assert.Multiple(() => {
            Assert.That(head, Is.Null);
            Assert.That(storage["/admin.css"], Is.Null);
            Assert.That(body, Does.Contain("1 removed"));
            Assert.That(body, Does.Contain("2 revision(s)"));
        });
    }

    [Test]
    public async Task AStrayThatVanishedIsAlreadyGoneAsync() {
        FakePageStore storage = new();
        await SeedAllAsync(storage);
        storage["/admin.css"] = new PageTemplate(Key("/admin.css"), "stray");
        Dictionary<string, StringValues> form = ScrubForm("/admin.css");

        using PageHarness pages = new(storage);
        await storage.ScrubStoredPathAsync(Key("/admin.css"));
        string body = await PostAsync(pages, form);

        Assert.That(body, Does.Contain("already gone"));
    }

    private static async Task SeedAllAsync(FakePageStore storage, string? except = null) {
        foreach (PagePath path in TestOptions.Seeds.Paths) {
            if (string.Equals(path.Key, except, StringComparison.Ordinal)) {
                continue;
            }

            storage[path.Key] = new PageTemplate(path, TestOptions.SeedSource(path)!) {
                SeedHash = TestOptions.Seeds.Find(path)?.Hash,
            };
        }
    }

    private static async Task<string> PreviewAsync(PageHarness pages, string? query = null) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", SeedsEndpoint.Path);
        if (query is not null) {
            context.Request.QueryString = new QueryString(query);
        }

        await SeedsEndpoint.GetAsync(context, pages.Editor, TestOptions.Seeds, pages.Storage,
            TestOptions.Reserved, new TestAntiforgery(), pages.Diff);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> PostAsync(PageHarness pages, Dictionary<string, StringValues> form) =>
        await TestHttp.ReadBodyAsync(await PostContextAsync(pages, form));

    private static async Task<DefaultHttpContext> PostContextAsync(
        PageHarness pages,
        Dictionary<string, StringValues> form) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", SeedsEndpoint.Path);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(form);

        await SeedsEndpoint.PostAsync(context, pages.Editor, TestOptions.Seeds, pages.Storage,
            TestOptions.Reserved, NullLoggerFactory.Instance);

        return context;
    }

    private static async Task<Dictionary<string, StringValues>> SeedFormAsync(
        FakePageStore storage,
        params string[] keys) {
        Dictionary<string, StringValues> form = new() {
            ["action"] = "apply",
        };
        List<string> selected = [];

        foreach (string key in keys) {
            SeedFile seed = TestOptions.Seeds.Find(Key(key))
                ?? throw new AssertionException($"{key} is not a shipped seed");
            PageHeadSummary? head = await storage.GetHeadAsync(Key(key));
            string number = IndexOf(key).ToString(CultureInfo.InvariantCulture);
            string prefix = "was." + number;

            form[prefix + ".key"] = key;
            form[prefix + ".deleted"] = head?.IsDeleted == true ? "1" : "0";
            form[prefix + ".anchor"] = head?.SeedHash ?? string.Empty;
            form[prefix + ".content"] = head?.ContentHash ?? string.Empty;
            form[prefix + ".seed"] = seed.Hash;
            selected.Add(number);
        }

        form["seed"] = selected.ToArray();
        return form;
    }

    private static Dictionary<string, StringValues> ScrubForm(params string[] keys) => new() {
        ["action"] = "scrub",
        ["stray"] = keys,
        ["confirm"] = "scrub",
    };

    private static int IndexOf(string key) {
        IReadOnlyList<PagePath> paths = TestOptions.Seeds.Paths;
        for (int index = 0; index < paths.Count; index++) {
            if (string.Equals(paths[index].Key, key, StringComparison.Ordinal)) {
                return index;
            }
        }

        throw new AssertionException($"{key} is not a shipped seed");
    }

    private static string Row(string body, string key) {
        int start = body.IndexOf($">{key}</a></th>", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no seed row for {key}");
        int end = body.IndexOf("</tr>", start, StringComparison.Ordinal);
        return body[start..end];
    }

    private static string StrayRow(string body, string key) {
        int start = body.IndexOf($"<th scope=\"row\">{key}</th>", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no shell-path row for {key}");
        int end = body.IndexOf("</tr>", start, StringComparison.Ordinal);
        return body[start..end];
    }

    private static void AssertBare(string body) => Assert.Multiple(() => {
        Assert.That(body, Does.Not.Contain("<link"), "no stylesheet the owner can hide it with");
        Assert.That(body, Does.Not.Contain("<script"), "no script the owner can break it with");
        Assert.That(body, Does.Contain("<a href=\"#main\">Skip to content</a>"));
        Assert.That(body, Does.Contain("<main id=\"main\">"));
    });

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
