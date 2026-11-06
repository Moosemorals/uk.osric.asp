// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class AdminSeedsEndpointTests {

    private readonly TestAntiforgery _antiforgery = new();

    [Test]
    public async Task ListRendersEverySeedWithItsStateAndRestoreFormsAsync() {
        FakePageStore storage = new();
        storage["/assistant.css"] = new PageTemplate(PagePath.Normalize("/assistant.css"), "/* edited */");
        using PageHarness pages = new(storage);
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin");

        await AdminSeedsEndpoint.ListAsync(context, pages.Editor, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            Assert.That(body, Does.Contain(
                $"<a href=\"{WebUtility.HtmlEncode(PageUrls.Editor(path))}\">{WebUtility.HtmlEncode(path.Key)}</a>"), path.Key);
        }

        Assert.That(body, Does.Contain("<td>Edited</td>"));
        Assert.That(body, Does.Contain("<td>Missing</td>"));
        Assert.That(body, Does.Contain("action=\"/admin/seeds/restore\""));
        Assert.That(body, Does.Contain("action=\"/admin/seeds/restore-all\""));
        Assert.That(body, Does.Contain("aria-label=\"Restore /assistant.css to its seed\""));
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("<th scope=\"col\">Page</th>"));
    }

    [Test]
    public async Task ListMarksADeletedSeedAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, "/* edited */");
        await storage.DeletePageAsync(path);
        using PageHarness pages = new(storage);
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin");

        await AdminSeedsEndpoint.ListAsync(context, pages.Editor, _antiforgery);

        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("<td>Deleted</td>"));
    }

    [Test]
    public async Task ListMarksASeedThatAlreadyMatchesAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(path))!;
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, seed);
        using PageHarness pages = new(storage);
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin");

        await AdminSeedsEndpoint.ListAsync(context, pages.Editor, _antiforgery);

        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("<td>Matches seed</td>"));
    }

    [Test]
    public async Task RestoreWritesTheSeedAndRedirectsAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(path))!;
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, "/* edited */");
        using PageHarness pages = new(storage);
        DefaultHttpContext context = PostContext("/admin/seeds/restore", ("path", path.Key));

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/admin"));
        Assert.That(storage[path.Key]?.Template, Is.EqualTo(seed));
        Assert.That(Session(context).GetString(AdminSeedsEndpoint.MessageKey), Is.EqualTo("Restored /assistant.css."));
    }

    [Test]
    public async Task RestoreLeavesAMatchingSeedAloneAsync() {
        PagePath path = PagePath.Normalize("/assistant.css");
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(path))!;
        FakePageStore storage = new();
        storage[path.Key] = new PageTemplate(path, seed);
        int writes = storage.WriteCount;
        using PageHarness pages = new(storage);
        DefaultHttpContext context = PostContext("/admin/seeds/restore", ("path", path.Key));

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        Assert.That(Session(context).GetString(AdminSeedsEndpoint.MessageKey), Is.EqualTo("/assistant.css already matched its seed."));
    }

    [Test]
    public async Task RestoreAllWritesEveryChangedSeedAsync() {
        PagePath edited = PagePath.Normalize("/assistant.css");
        PagePath deleted = PagePath.Normalize("/editor.css");
        FakePageStore storage = new();
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            storage[path.Key] = new PageTemplate(path, (await PageAssetSeeder.ReadSeedTemplateAsync(path))!);
        }

        storage[edited.Key] = new PageTemplate(edited, "/* edited */");
        await storage.DeletePageAsync(deleted);
        using PageHarness pages = new(storage);
        DefaultHttpContext context = PostContext("/admin/seeds/restore-all");

        await AdminSeedsEndpoint.RestoreAllAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/admin"));
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            Assert.That(storage[path.Key]?.Template, Is.EqualTo((await PageAssetSeeder.ReadSeedTemplateAsync(path))!), path.Key);
        }

        Assert.That(Session(context).GetString(AdminSeedsEndpoint.MessageKey), Is.EqualTo("Restored 2 seeded pages; 6 already matched."));
    }

    [Test]
    public async Task RestoreRejectsAnUnknownSeedAsync() {
        using PageHarness pages = new(new FakePageStore());
        DefaultHttpContext context = PostContext("/admin/seeds/restore", ("path", "/not-a-seed"));

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task RestoreRejectsAMissingPathAsync() {
        using PageHarness pages = new(new FakePageStore());
        DefaultHttpContext context = PostContext("/admin/seeds/restore", ("path", " "));

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task RestoreRejectsAnInvalidAntiforgeryTokenAsync() {
        using PageHarness pages = new(new FakePageStore());
        DefaultHttpContext context = PostContext("/admin/seeds/restore", ("path", "/assistant.css"));
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task RestoreRejectsARequestThatIsNotAFormAsync() {
        using PageHarness pages = new(new FakePageStore());
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/admin/seeds/restore");

        await AdminSeedsEndpoint.RestoreAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task RestoreAllRejectsAnInvalidAntiforgeryTokenAsync() {
        using PageHarness pages = new(new FakePageStore());
        DefaultHttpContext context = PostContext("/admin/seeds/restore-all");
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));

        await AdminSeedsEndpoint.RestoreAllAsync(context, pages.Editor);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    private static DefaultHttpContext PostContext(string requestPath, params (string Name, string Value)[] fields) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", requestPath);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(fields.ToDictionary(
            field => field.Name,
            field => new StringValues(field.Value)));
        return context;
    }

    private static TestSession Session(DefaultHttpContext context) {
        return (TestSession)context.Features.Get<ISessionFeature>()!.Session;
    }
}
