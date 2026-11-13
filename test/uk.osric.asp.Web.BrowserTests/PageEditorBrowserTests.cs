// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>The seeded `/pages` in a browser: create, load, edit, save, view, and the ways it refuses.</summary>
[TestFixture]
internal sealed class PageEditorBrowserTests : BrowserTest {

    [Test]
    public async Task CreatingAPageFromTheListingOpensItsEditorAsync() {
        string path = UniquePath("page-created");

        await Page.GotoAsync("pages");
        await Page.FillAsync("#new-page-path", path);
        await Page.ClickAsync("button[type=submit]");
        await Page.WaitForURLAsync("**/pages?edit=**");

        Assert.That(await Page.InputValueAsync("#template"), Is.Empty, "a new page starts empty");
    }

    [Test]
    public async Task TheEditorShowsTheSourceItStoredAsync() {
        string path = UniquePath("page-loaded");
        string template = PageTemplate("Loaded for editing", "<p>the stored body</p>");

        await SaveAsync(path, template);
        await Page.GotoAsync(EditUrl(path));

        Assert.That(await Page.InputValueAsync("#template"), Is.EqualTo(template));
    }

    [Test]
    public async Task SavingAnEditIsServedOnTheNextRequestAsync() {
        string path = UniquePath("page-edited");

        await SaveAsync(path, PageTemplate("Browser first"));
        await Page.FillAsync("#template", PageTemplate("Browser second"));
        await Page.ClickAsync("button[name=cmd][value=save]");
        await ExpectMessageAsync("Saved as revision");

        await Page.GotoAsync(path.TrimStart('/'));

        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Browser second");
    }

    [Test]
    public async Task SaveAndViewLandsOnThePageItSavedAsync() {
        string path = UniquePath("page-viewed");

        await Page.GotoAsync(EditUrl(path));
        await Page.FillAsync("#template", PageTemplate("Saved and shown"));
        await Page.ClickAsync("button[name=cmd][value=save-and-view]");
        await Page.WaitForURLAsync($"**{path}");

        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Saved and shown");
    }

    [Test]
    public async Task ASaveWithAnErrorKeepsTheTemplateAndShowsWhereItBrokeAsync() {
        string path = UniquePath("page-broken");

        await Page.GotoAsync(EditUrl(path));
        await Page.FillAsync("#template", "<% this is not C# %>");
        await Page.ClickAsync("button[name=cmd][value=save]");

        await ExpectMessageAsync("does not compile");
        Assert.That(await Page.InputValueAsync("#template"), Is.EqualTo("<% this is not C# %>"));
        await Assertions.Expect(Page.Locator("ul.diagnostics li").First).ToBeVisibleAsync();

        IResponse? served = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(served?.Status, Is.GreaterThanOrEqualTo(500), "a page that does not compile is not served");
    }

    [Test]
    public async Task ASaveFromAStaleFormIsRefusedAndKeepsTheDraftAsync() {
        string path = UniquePath("page-stale");
        await SaveAsync(path, PageTemplate("Stored by the first form"));
        await Page.GotoAsync(EditUrl(path));
        await Page.FillAsync("#template", PageTemplate("My unsaved draft"));

        // A second save lands first, using this form's own token and the head it was rendered from.
        await Page.EvaluateAsync("""
            async () => {
                const form = document.querySelector('.editor-form');
                const fields = new URLSearchParams(new FormData(form));
                fields.set('template', '<h1>Stored by the racing save</h1>');
                const response = await fetch(`/pages${location.search}`, { method: 'POST', body: fields });
                if (!response.ok) {
                    throw new Error(`the racing save answered ${response.status}`);
                }
            }
            """);

        IResponse? refused = await Page.RunAndWaitForResponseAsync(
            () => Page.ClickAsync("button[name=cmd][value=save]"),
            response => string.Equals(response.Request.Method, "POST", StringComparison.Ordinal)
                && response.Url.Contains("/pages", StringComparison.Ordinal));

        Assert.Multiple(() => {
            Assert.That(refused?.Status, Is.EqualTo(409), "the head moved under the form");
        });
        await ExpectMessageAsync("nothing was saved");
        Assert.That(await Page.InputValueAsync("#template"), Does.Contain("My unsaved draft"));
        await ExpectMessageAsync("Stored by the racing save");
    }

    [Test]
    public async Task AnAnonymousVisitorIsSentToTheSignInPageAsync() {
        await using IBrowserContext anonymous = await BrowserSuite.Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = Host.BaseAddress.ToString(),
        });
        IPage page = await anonymous.NewPageAsync();

        await page.GotoAsync("pages");

        Assert.That(page.Url, Does.Contain("/login?"));
        Assert.That(page.Url, Does.Contain("returnUrl=%2Fpages"));
    }

    private static string EditUrl(string path) => "pages?edit=" + Uri.EscapeDataString(path);

    // The page answers a save in place, so waiting on the message is waiting for the request.
    private async Task SaveAsync(string path, string template) {
        await Page.GotoAsync(EditUrl(path));
        await Page.FillAsync("#template", template);
        await Page.ClickAsync("button[name=cmd][value=save]");
        await ExpectMessageAsync("Saved as revision");
    }
}
