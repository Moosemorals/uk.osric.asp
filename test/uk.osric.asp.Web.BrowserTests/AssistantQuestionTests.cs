// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>A tool call that stops until the owner answers, and the card that asks them.</summary>
[TestFixture]
internal sealed class AssistantQuestionTests : BrowserTest {

    private const string Ask = """
        {"questions":[
          {"question":"Which paragraph should the page show?","header":"Paragraph",
           "options":[{"label":"First","description":"The short one"},{"label":"Second"}]},
          {"question":"Anything to add?"}]}
        """;

    [Test]
    public async Task AQuestionCardIsAnsweredAndTheRunContinuesAsync() {
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call-question", "ask_user", Ask),
            StubResponse.Words("The answer is recorded.", TimeSpan.Zero));

        await SendAsync("Ask me a question");
        await Assertions.Expect(Page.Locator(".assistant-question"))
            .ToContainTextAsync("Which paragraph should the page show?", new() { Timeout = 60_000 });
        await Page.CheckAsync(".assistant-question input[value='First']");
        await Page.FillAsync(".assistant-question input[name='other1']", "Nothing else");
        await Page.ClickAsync(".assistant-question button[type=submit]");

        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("The answer is recorded.", new() { Timeout = 60_000 });
        // The run's completion reloads the page, and the reload builds the answered card from the record.
        await Assertions.Expect(Page.Locator(".assistant-question-answers"))
            .ToContainTextAsync("First", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator(".assistant-question-answers")).ToContainTextAsync("Nothing else");
    }

    [Test]
    public async Task TheCardIsRebuiltByAReloadAndOnlyOnceAsync() {
        long id = await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call-question", "ask_user", Ask),
            StubResponse.Words("The answer is recorded.", TimeSpan.Zero));

        await SendAsync("Ask me a question");
        await Assertions.Expect(Page.Locator(".assistant-question")).ToContainTextAsync("Which paragraph", new() { Timeout = 60_000 });
        await Page.GotoAsync($"assistant/{id}");

        await Assertions.Expect(Page.Locator(".assistant-question")).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        Assert.That(await Page.Locator(".assistant-question form").CountAsync(), Is.EqualTo(1));

        await Page.CheckAsync(".assistant-question input[value='Second']");
        await Page.ClickAsync(".assistant-question button[type=submit]");
        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("The answer is recorded.", new() { Timeout = 60_000 });
    }

    [Test]
    public async Task TheAnsweredQuestionReadsBackAfterTheRunAsync() {
        long id = await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call-question", "ask_user", Ask),
            StubResponse.Words("The answer is recorded.", TimeSpan.Zero));

        await SendAsync("Ask me a question");
        await Assertions.Expect(Page.Locator(".assistant-question")).ToContainTextAsync("Which paragraph", new() { Timeout = 60_000 });
        // The second question is left blank on purpose: a blank is an answer the record keeps.
        await Page.CheckAsync(".assistant-question input[value='Second']");
        await Page.ClickAsync(".assistant-question button[type=submit]");
        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("The answer is recorded.", new() { Timeout = 60_000 });

        await Page.GotoAsync($"assistant/{id}");
        await Assertions.Expect(Page.Locator(".assistant-question-answers")).ToContainTextAsync("Second", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator(".assistant-question-answers")).ToContainTextAsync("No answer");
    }
}
