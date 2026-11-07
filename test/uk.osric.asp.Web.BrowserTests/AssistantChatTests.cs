// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

/// <summary>The assistant conversation page, driven through its own script against a scripted provider.</summary>
[TestFixture]
internal sealed class AssistantChatTests : BrowserTest {

    [Test]
    public async Task SendingAMessageStreamsTheAnswerIntoTheTranscriptAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Words("The answer arrived in one piece.", TimeSpan.FromMilliseconds(40)));

        await SendAsync("Say something");

        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("The answer arrived in one piece.", new() { Timeout = 30_000 });
    }

    [Test]
    public async Task TheConversationTakesItsTitleFromTheFirstMessageAsync() {
        long id = await NewConversationAsync();
        Provider.Script(StubResponse.Words("Noted.", TimeSpan.Zero));

        await SendAsync("Name this conversation");

        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Name this conversation", new() { Timeout = 30_000 });
        await Page.GotoAsync("assistant");
        await Assertions.Expect(Page.Locator($"[href='/assistant/{id}']")).ToHaveTextAsync("Name this conversation");
    }

    [Test]
    public async Task ThinkingIsKeptCollapsedOnceTheAnswerIsCompleteAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Thinking("I should think about this carefully", "Here is the answer.", TimeSpan.FromMilliseconds(30)));

        await SendAsync("Think about it");

        ILocator reasoning = Page.Locator("details.assistant-reasoning");
        await Assertions.Expect(reasoning).ToContainTextAsync("I should think about this carefully", new() { Timeout = 30_000 });
        await Assertions.Expect(reasoning).Not.ToHaveAttributeAsync("open", string.Empty);
    }

    [Test]
    public async Task TypingFillsTheMessageBudgetAsync() {
        await NewConversationAsync();

        await Page.FillAsync("#assistant-message", "12345");

        await Assertions.Expect(Page.Locator("#assistant-budget-message")).ToHaveTextAsync("5");
        double value = await Page.Locator("#assistant-budget-message-meter").EvaluateAsync<double>("meter => meter.value");
        Assert.That(value, Is.EqualTo(5));
    }

    [Test]
    public async Task SavingOptionsChangesTheTitleAndTheApprovalModeAsync() {
        long id = await NewConversationAsync();

        await Page.FillAsync("#conversation-title", "Renamed by a browser");
        await Page.SelectOptionAsync("#approval-mode", "RequireApproval");
        await Page.ClickAsync("#assistant-options-form button[type=submit]");

        await Assertions.Expect(Page.Locator("#assistant-options-status")).ToHaveTextAsync("Page options saved");
        await Page.GotoAsync($"assistant/{id}");
        Assert.Multiple(() => {
            Assert.That(Page.InputValueAsync("#conversation-title").Result, Is.EqualTo("Renamed by a browser"));
            Assert.That(Page.InputValueAsync("#approval-mode").Result, Is.EqualTo("RequireApproval"));
        });
    }

    [Test]
    public async Task DeletingAConversationTakesItOffTheListAsync() {
        long id = await NewConversationAsync();

        await Page.GotoAsync($"assistant/{id}/delete");
        await Page.ClickAsync("button[type=submit]");

        await Page.WaitForURLAsync("**/assistant");
        await Assertions.Expect(Page.Locator("main")).Not.ToContainTextAsync($"/assistant/{id}");
    }

    [Test]
    public async Task AStoppedRunKeepsWhatItHadAlreadyWrittenAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Words(string.Join(' ', Enumerable.Repeat("word", 200)), TimeSpan.FromMilliseconds(100)));

        await SendAsync("Write a lot, slowly");
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("word word", new() { Timeout = 30_000 });
        await Page.ClickAsync("#assistant-stop-form button[type=submit]");

        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("This answer was stopped.", new() { Timeout = 30_000 });
    }

    [Test]
    public async Task AToolOnlyStepLeavesNoBlankRowWhileTheAnswerStreamsAsync() {
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("blank-row-call", "read_page", """{"path":"/"}"""),
            new StubResponse { Delay = TimeSpan.FromSeconds(3), Chunks = StubResponse.Words("I read the page.", TimeSpan.Zero).Chunks });

        await SendAsync("Read the home page");
        await Assertions.Expect(Page.Locator("details.assistant-tool")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        int blankRows = await Page.Locator("article.assistant-message:not(.user-message)").EvaluateAllAsync<int>(
            "nodes => nodes.filter(node => !Array.from(node.querySelectorAll('pre')).some(pre => pre.textContent.trim())).length");
        Assert.That(blankRows, Is.Zero, "a step that only calls a tool must not add a blank row");
    }
}
