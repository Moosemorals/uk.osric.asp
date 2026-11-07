// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;
using NUnit.Framework;

/// <summary>The editor end to end: create, save, read history, delete, recreate and scrub.</summary>
[TestFixture]
internal sealed class EditorFlowTests : BrowserTest {

    [Test]
    public async Task CreatingAPageFromTheListingOpensItsEditorAsync() {
        string path = UniquePath("created");

        await Page.GotoAsync("edit");
        await Page.FillAsync("#new-page-path", path);
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync($"**/edit{path}");
        Assert.That(await Page.InputValueAsync("#template"), Is.Empty);
    }

    [Test]
    public async Task SavingATemplateRendersItAsAPageAsync() {
        string path = UniquePath("saved");

        await SavePageAsync(path, "<h1>Saved by a browser</h1><p><%= ctx.Path %></p>");
        IResponse? response = await Page.GotoAsync(path.TrimStart('/'));

        Assert.That(response?.Status, Is.EqualTo(200));
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Saved by a browser");
        await Assertions.Expect(Page.Locator("p")).ToHaveTextAsync(path);
    }

    [Test]
    public async Task SaveAndViewLandsOnTheSavedPageAsync() {
        string path = UniquePath("save-and-view");

        await Page.GotoAsync($"edit/{path.TrimStart('/')}");
        await Page.FillAsync("#template", "<h1>Saved and shown</h1>");
        await Page.ClickAsync("button[name=cmd][value=save-and-view]");

        await Page.WaitForURLAsync($"**/{path.TrimStart('/')}");
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Saved and shown");
    }

    [Test]
    public async Task SavingTwiceListsBothRevisionsInHistoryAsync() {
        string path = UniquePath("history");

        await SavePageAsync(path, "<p>first draft</p>");
        await SavePageAsync(path, "<p>second draft</p>");
        await Page.GotoAsync($"edit{path}?cmd=history");

        await Assertions.Expect(Page.Locator("table tbody tr")).ToHaveCountAsync(2);
        await Assertions.Expect(Page.Locator("table tbody tr").First).ToContainTextAsync("Current");
    }

    [Test]
    public async Task ARevisionPageShowsTheTemplateSavedAtThatRevisionAsync() {
        string path = UniquePath("revision");

        await SavePageAsync(path, "<p>first draft</p>");
        await SavePageAsync(path, "<p>second draft</p>");
        await Page.GotoAsync($"edit{path}?cmd=history");
        await Page.Locator("table tbody tr").Last.Locator("a").ClickAsync();

        await Assertions.Expect(Page.Locator("pre.source")).ToContainTextAsync("first draft");
    }

    [Test]
    public async Task DeletingAPageHidesItAndKeepsItInTheDeletedListAsync() {
        string path = UniquePath("deleted");

        await SavePageAsync(path, "<p>here for a moment</p>");
        await Page.GotoAsync($"edit{path}?cmd=delete");
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync("**/edit");
        await ExpectMessageAsync($"Deleted {path}; revision history is retained.");
        IResponse? gone = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(gone?.Status, Is.EqualTo(404));
        await Page.GotoAsync("edit");
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync(path);
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync("Scrub history");
    }

    [Test]
    public async Task RecreatingADeletedPageBringsItBackAsync() {
        string path = UniquePath("restored");

        await SavePageAsync(path, "<p>before the delete</p>");
        await Page.GotoAsync($"edit{path}?cmd=delete");
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync("**/edit");

        await SavePageAsync(path, "<p>after the delete</p>");
        IResponse? live = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(live?.Status, Is.EqualTo(200));
        await Assertions.Expect(Page.Locator("p")).ToHaveTextAsync("after the delete");
    }

    [Test]
    public async Task ScrubbingADeletedPageRemovesItFromTheListingAsync() {
        string path = UniquePath("scrubbed");

        await SavePageAsync(path, "<p>forget me</p>");
        await Page.GotoAsync($"edit{path}?cmd=delete");
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync("**/edit");
        await Page.GotoAsync($"edit{path}?cmd=scrub");
        await Page.FillAsync("#confirmation", path);
        await Page.ClickAsync("button[type=submit]");

        await ExpectMessageAsync($"Scrubbed {path} and its retained history.");
        await Page.GotoAsync("edit");
        await Assertions.Expect(Page.Locator("main")).Not.ToContainTextAsync(path);
    }

    [Test]
    public async Task ScrubbingNeedsThePagePathTypedExactlyAsync() {
        string path = UniquePath("confirmed");

        await SavePageAsync(path, "<p>forget me</p>");
        await Page.GotoAsync($"edit{path}?cmd=delete");
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync("**/edit");
        await Page.GotoAsync($"edit{path}?cmd=scrub");
        await Page.FillAsync("#confirmation", "not-the-path");
        IResponse? rejected = await Page.RunAndWaitForResponseAsync(
            () => Page.ClickAsync("button[type=submit]"),
            response => response.Url.EndsWith($"/edit{path}?cmd=scrub", StringComparison.Ordinal));

        Assert.That(rejected?.Status, Is.EqualTo(400));
        await Page.GotoAsync("edit");
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync(path);
    }

    [Test]
    public async Task GeneratedSourceIsShownForASavedPageAsync() {
        string path = UniquePath("generated");

        await SavePageAsync(path, "<p><%= ctx.Path %></p>");
        await Page.GotoAsync($"edit{path}?cmd=generated");

        await Assertions.Expect(Page.Locator("pre.source")).ToContainTextAsync("class");
    }

    [Test]
    public async Task ARestoredSeedPageComesBackWithItsBundledSourceAsync() {
        string path = "/rocket.svg";

        await Page.GotoAsync($"edit{path}?cmd=delete");
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync("**/edit");
        await Page.GotoAsync($"admin/restore?path={Uri.EscapeDataString(path)}");
        await Page.ClickAsync("button[type=submit]");

        IResponse? restored = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(restored?.Status, Is.EqualTo(200).Or.EqualTo(304));
        Assert.That(await Page.ContentAsync(), Does.Contain("<svg"));
    }

    [Test]
    public async Task ASaveWithACompileErrorKeepsTheTemplateAndReportsDiagnosticsAsync() {
        string path = UniquePath("broken");

        await Page.GotoAsync($"edit{path}");
        await Page.FillAsync("#template", "<p><%= this is not C# %></p>");
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync($"**/edit{path}");
        await ExpectMessageAsync("Saved with errors.");
        Assert.That(await Page.InputValueAsync("#template"), Does.Contain("this is not C#"));
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync("error");
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentToTheSignInPageAsync() {
        await using IBrowserContext anonymous = await BrowserSuite.Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = Host.BaseAddress.ToString(),
        });
        IPage page = await anonymous.NewPageAsync();

        await page.GotoAsync("edit");

        Assert.That(page.Url, Does.Contain("/login?"));
    }
}
