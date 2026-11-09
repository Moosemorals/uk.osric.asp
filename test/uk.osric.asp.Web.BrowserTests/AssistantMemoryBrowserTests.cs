// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>The owner's memory page, driven in a browser: see, correct, pin and forget a memory.</summary>
[TestFixture]
internal sealed class AssistantMemoryBrowserTests : BrowserTest {

    [Test]
    public async Task AMemoryIsListedCorrectedPinnedAndForgottenAsync() {
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("remember-call", "remember",
                """{"kind":"preference","content":"The owner writes British English."}"""),
            StubResponse.Words("Noted.", TimeSpan.Zero));

        await SendAsync("Please remember that I write British English.");
        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("Noted.", new() { Timeout = 30_000 });

        // The page lists what the assistant wrote, and its row is a form holding the note.
        await Page.GotoAsync("assistant/memory");
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Memory");
        ILocator row = Page.Locator(".assistant-memory").First;
        await Assertions.Expect(row.Locator("textarea")).ToHaveValueAsync("The owner writes British English.");
        await Assertions.Expect(Page.Locator("h2")).ToHaveTextAsync("Preferences");

        // Correct the note, change its kind and pin it, all in the one form.
        await row.Locator("textarea").FillAsync("The owner writes British English and prefers colour.");
        await row.Locator("select").SelectOptionAsync("feedback");
        await row.Locator("input[type=checkbox]").CheckAsync();
        await row.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(Page.Locator(".assistant-memory textarea"))
            .ToHaveValueAsync("The owner writes British English and prefers colour.", new() { Timeout = 30_000 });
        await Assertions.Expect(Page.Locator(".assistant-memory-pinned")).ToHaveTextAsync("Pinned");
        await Assertions.Expect(Page.Locator("h2")).ToHaveTextAsync("Feedback");

        // Forgetting asks on its own page first, and then the list is empty.
        await Page.ClickAsync(".assistant-memory-forget a");
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Forget memory");
        await Page.ClickAsync("text=Forget this memory");

        await Assertions.Expect(Page.Locator(".assistant-memory")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync("has not remembered anything yet");
    }
}
