// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>What a page render is offered of the jobs: the capability, the layout's none, and a HEAD.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageJobRenderTests {

    private readonly FakePageStore _storage = new();
    private readonly TestAntiforgery _antiforgery = new();

    [Test]
    public async Task APageReachesTheJobCapabilityAsync() {
        using PageHarness pages = new(_storage);
        await pages.Editor.SaveAsync(PageHarness.Key("/jobs"), """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               ctx.Response.SetHeader("X-Jobs", ctx.Capabilities.Get<IPageJobs>() is null ? "none" : "present"); %>ok
            """);

        DefaultHttpContext context = await RenderAsync(pages, "/jobs");

        Assert.That(context.Response.Headers["X-Jobs"].ToString(), Is.EqualTo("present"));
    }

    [Test]
    public async Task ALayoutIsOfferedNoJobsAsync() {
        using PageHarness pages = new(_storage);
        await pages.Editor.SaveAsync(PageHarness.Key("/_layout"), """
            <%! using System.Collections.Generic; %>
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Title = "Page"; %><p>layout-jobs:<%= ctx.Capabilities.Get<IPageJobs>() is null ? "none" : "present" %></p><%== layout.Body %>
            """);
        await pages.Editor.SaveAsync(PageHarness.Key("/jobs"), """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>hi
            """);

        DefaultHttpContext context = await RenderAsync(pages, "/jobs", layout: "/_layout", user: "owner");

        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("layout-jobs:none"),
            "a layout that started a job would start one per page it wraps");
    }

    [Test]
    public async Task AHeadRenderIsRefusedAStartAsync() {
        using PageHarness pages = new(_storage);
        await pages.Editor.SaveAsync(PageHarness.Key("/jobs"), """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               PageJobStart start = await ctx.Capabilities.GetRequired<IPageJobs>()
                   .StartAsync("/_work/echo", null);
               ctx.Response.SetHeader("X-Start", start.Refusal.ToString()); %>ok
            """);

        DefaultHttpContext context = await RenderAsync(pages, "/jobs", method: "HEAD");

        Assert.That(context.Response.Headers["X-Start"].ToString(), Is.EqualTo("ReadOnly"),
            "a HEAD runs the page to produce its representation, and starting work is a side effect");
    }

    [Test]
    public async Task AHeadRenderStillReadsItsJobsAsync() {
        using PageHarness pages = new(_storage);
        await pages.Editor.SaveAsync(PageHarness.Key("/jobs"), """
            <%! using System.Collections.Generic; %>
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               IReadOnlyList<PageJobInfo> jobs = await ctx.Capabilities.GetRequired<IPageJobs>().ListAsync();
               ctx.Response.SetHeader("X-Jobs", jobs.Count.ToString()); %>ok
            """);

        DefaultHttpContext context = await RenderAsync(pages, "/jobs", method: "HEAD");

        Assert.That(context.Response.Headers["X-Jobs"].ToString(), Is.EqualTo("0"),
            "a HEAD is offered the reads and not the writes");
    }

    private async Task<DefaultHttpContext> RenderAsync(
        PageHarness pages,
        string path,
        string method = "GET",
        string? layout = null,
        string? user = null) {
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), method, path);
        if (user is not null) {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
        }

        await PageEndpoint.HandleAsync(
            context,
            pages.Renderer,
            NullLoggerFactory.Instance,
            pages.CreateEnvironment(_antiforgery, layout: layout ?? "/_layout"));

        return context;
    }
}
