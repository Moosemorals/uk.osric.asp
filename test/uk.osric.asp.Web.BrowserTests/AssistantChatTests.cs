// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

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
    public async Task AMarkdownReplyIsFormattedWhenTheStepCompletesAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Words("**Bold** and [about](/about).", TimeSpan.Zero));

        await SendAsync("Use markdown");

        await Assertions.Expect(Page.Locator(".assistant-content strong"))
            .ToHaveTextAsync("Bold", new() { Timeout = 30_000 });
        await Assertions.Expect(Page.Locator(".assistant-content a[href='/about']"))
            .ToHaveTextAsync("about", new() { Timeout = 30_000 });
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
    public async Task TypingALongMessageShowsTheFigureInThousandsAsync() {
        await NewConversationAsync();

        await Page.EvaluateAsync("""
            const input = document.getElementById('assistant-message');
            input.value = 'x'.repeat(12345);
            input.dispatchEvent(new Event('input'));
            """);

        await Assertions.Expect(Page.Locator("#assistant-budget-message")).ToHaveTextAsync("12k");
    }

    [Test]
    public async Task TheButtonsLeadTheCountsLineAndTheCountTrailsItAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Words(string.Join(' ', Enumerable.Repeat("word", 200)), TimeSpan.FromMilliseconds(100)));
        await SendAsync("Write a lot, slowly");
        await Assertions.Expect(Page.Locator("#assistant-stop")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        double[][] boxes = await Page.EvaluateAsync<double[][]>("""
            () => ['.assistant-compose-actions', '#assistant-send', '#assistant-stop', '.assistant-char-count'].map(selector => {
                const r = document.querySelector(selector).getBoundingClientRect();
                return [r.top, r.left, r.right];
            })
            """);

        Assert.That(boxes[2][0], Is.EqualTo(boxes[1][0]).Within(4), "Stop does not sit beside Send");
        Assert.That(boxes[3][0], Is.EqualTo(boxes[1][0]).Within(4), "the count is not on the buttons' line");
        Assert.That(boxes[1][1], Is.EqualTo(boxes[0][1]).Within(1), "Send is not at the line start");
        Assert.That(boxes[3][2], Is.EqualTo(boxes[0][2]).Within(1), "the count is not at the line end");
    }

    [Test]
    public async Task TheStepBudgetCountsUpWhileTheAnswerStreamsAsync() {
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("step-count-call", "read_page", """{"path":"/"}"""),
            new StubResponse { Delay = TimeSpan.FromSeconds(5), Chunks = StubResponse.Words("I read the page.", TimeSpan.Zero).Chunks });

        await SendAsync("Read the home page");

        // The second step is spent as it starts, so the row moves before its answer has finished.
        await Assertions.Expect(Page.Locator("#assistant-budget-steps")).ToHaveTextAsync("2", new() { Timeout = 30_000 });
        await Assertions.Expect(Page.Locator("#assistant-chat")).ToHaveAttributeAsync("aria-busy", "true");
    }

    [Test]
    public async Task AToolCallRaisesTheToolCallBudgetBeforeTheRunCompletesAsync() {
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("tool-budget-call", "read_page", """{"path":"/"}"""),
            new StubResponse { Delay = TimeSpan.FromSeconds(5), Chunks = StubResponse.Words("I read the page.", TimeSpan.Zero).Chunks });

        await SendAsync("Read the home page");

        await Assertions.Expect(Page.Locator("#assistant-budget-tool-calls")).ToHaveTextAsync("1", new() { Timeout = 30_000 });
        await Assertions.Expect(Page.Locator("#assistant-chat")).ToHaveAttributeAsync("aria-busy", "true");
    }

    [Test]
    public async Task TypingIsNotOverwrittenByABudgetUpdateAsync() {
        await NewConversationAsync();
        // The first step is held open so the typing happens while the run is running but before the
        // update that would overwrite it, which is the whole point of the test.
        Provider.Script(
            new StubResponse { Delay = TimeSpan.FromSeconds(3), ToolCall = ("typing-call", "read_page", """{"path":"/"}""") },
            new StubResponse { Delay = TimeSpan.FromSeconds(3), Chunks = StubResponse.Words("I read the page.", TimeSpan.Zero).Chunks });

        await SendAsync("Read the home page");
        await Assertions.Expect(Page.Locator("#assistant-budget-steps")).ToHaveTextAsync("1", new() { Timeout = 30_000 });

        // The textarea is the message row's only source, so the updates that follow must leave it alone.
        await Page.EvaluateAsync("""
            const input = document.getElementById('assistant-message');
            input.value = '12345';
            input.dispatchEvent(new Event('input'));
            """);
        await Assertions.Expect(Page.Locator("#assistant-budget-steps")).ToHaveTextAsync("2", new() { Timeout = 30_000 });

        await Assertions.Expect(Page.Locator("#assistant-budget-message")).ToHaveTextAsync("5");
        await Assertions.Expect(Page.Locator("#assistant-chat")).ToHaveAttributeAsync("aria-busy", "true");
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
    public async Task DeletingAConversationFromTheListTakesItOffTheListAsync() {
        long kept = await NewConversationAsync();
        long deleted = await NewConversationAsync();

        await Page.GotoAsync("assistant");
        await Page.Locator($"form.assistant-conversation-delete[action='/assistant/{deleted}/delete'] button").ClickAsync();

        await Page.WaitForURLAsync("**/assistant");
        await Assertions.Expect(Page.Locator($"[href='/assistant/{deleted}']")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator($"[href='/assistant/{kept}']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task AStoppedRunKeepsWhatItHadAlreadyWrittenAsync() {
        await NewConversationAsync();
        Provider.Script(StubResponse.Words(string.Join(' ', Enumerable.Repeat("word", 200)), TimeSpan.FromMilliseconds(100)));

        await SendAsync("Write a lot, slowly");
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("word word", new() { Timeout = 30_000 });
        await Page.ClickAsync("#assistant-stop");

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
