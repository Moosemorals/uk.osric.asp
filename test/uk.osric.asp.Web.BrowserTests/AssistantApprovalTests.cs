// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;
using System.Globalization;

/// <summary>A tool call that needs the owner's decision before it touches a page.</summary>
[TestFixture]
internal sealed class AssistantApprovalTests : BrowserTest {

    [Test]
    public async Task AnApprovedToolCallRunsAndWritesThePageAsync() {
        string path = UniquePath("approved");
        await StartConversationRequiringApprovalAsync();
        Provider.Script(
            StubResponse.Tool("call-approve", "write_page", $$"""{"path":"{{path}}","content":"<p>approved by the owner</p>"}"""),
            StubResponse.Words("The page is written.", TimeSpan.Zero));

        await SendAsync($"Write {path}");
        await Assertions.Expect(Page.Locator(".assistant-approval")).ToContainTextAsync("write_page", new() { Timeout = 60_000 });
        await Page.Locator(".assistant-approval button", new() { HasTextString = "Approve" }).ClickAsync();
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("The page is written.", new() { Timeout = 60_000 });

        IResponse? written = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(written?.Status, Is.EqualTo(200));
        await Assertions.Expect(Page.Locator("p")).ToHaveTextAsync("approved by the owner");
    }

    [Test]
    public async Task ADeniedToolCallLeavesThePageAloneAsync() {
        string path = UniquePath("denied");
        await StartConversationRequiringApprovalAsync();
        Provider.Script(
            StubResponse.Tool("call-deny", "write_page", $$"""{"path":"{{path}}","content":"<p>should never exist</p>"}"""),
            StubResponse.Words("The write was denied.", TimeSpan.Zero));

        await SendAsync($"Write {path}");
        await Assertions.Expect(Page.Locator(".assistant-approval")).ToContainTextAsync("write_page", new() { Timeout = 60_000 });
        await Page.Locator(".assistant-approval button", new() { HasTextString = "Deny" }).ClickAsync();
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("The write was denied.", new() { Timeout = 60_000 });

        IResponse? missing = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(missing?.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task APendingApprovalSurvivesAReloadAsync() {
        string path = UniquePath("pending");
        long id = await StartConversationRequiringApprovalAsync();
        Provider.Script(
            StubResponse.Tool("call-pending", "write_page", $$"""{"path":"{{path}}","content":"<p>waiting</p>"}"""),
            StubResponse.Words("Done.", TimeSpan.Zero));

        await SendAsync($"Write {path}");
        await Assertions.Expect(Page.Locator(".assistant-approval")).ToContainTextAsync("write_page", new() { Timeout = 60_000 });
        await Page.GotoAsync($"assistant/{id}");

        await Assertions.Expect(Page.Locator("section.assistant-approvals")).ToContainTextAsync("write_page", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("section.assistant-approvals button")).ToHaveCountAsync(2);
    }

    /// <summary>Opens a conversation, switches it to approval mode, and returns its id.</summary>
    private async Task<long> StartConversationRequiringApprovalAsync() {
        await Page.GotoAsync("assistant");
        await Page.ClickAsync("text=New conversation");
        await Page.WaitForURLAsync(AssistantUrl());
        await Page.SelectOptionAsync("#approval-mode", "RequireApproval");
        await Page.ClickAsync("#assistant-options-form button[type=submit]");
        await Assertions.Expect(Page.Locator("#assistant-options-status")).ToHaveTextAsync("Page options saved");
        return long.Parse(Page.Url.Split('/')[^1], CultureInfo.InvariantCulture);
    }
}
