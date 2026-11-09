// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>A run that runs out of steps asks the page's owner whether to carry on.</summary>
[TestFixture]
internal sealed class AssistantContinuationTests : BrowserTest {

    private BrowserHost _host = null!;

    protected override BrowserHost Host => _host;

    [OneTimeSetUp]
    public async Task StartHostAsync() =>
        // One step is the whole budget, so the run pauses after its tool call and the second script
        // is the answer the owner's continuation unlocks.
        _host = await BrowserHost.StartAsync(new BrowserHostSettings {
            MaxSteps = 1,
            HeartbeatSeconds = 1,
        });

    [OneTimeTearDown]
    public async Task StopHostAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RunningOutOfStepsAsksTheOwnerAndContinueCarriesOnAsync() {
        await SignInAsync();
        long id = await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call-read", "read_page", """{"path":"/a-page-that-is-not-there"}"""),
            StubResponse.Words("The page is missing.", TimeSpan.Zero));

        await SendAsync("Read the missing page");

        await Assertions.Expect(Page.Locator("section.assistant-continuation"))
            .ToContainTextAsync("Do you want to continue?", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("section.assistant-continuation"))
            .ToContainTextAsync("used all 1 of its steps");

        await Page.Locator("section.assistant-continuation button", new() { HasTextString = "Continue" }).ClickAsync();

        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("The page is missing.", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("section.assistant-continuation")).ToHaveCountAsync(0);

        // After a reload the question is gone and the budget row names the widened limit.
        await Page.GotoAsync($"assistant/{id}");
        await Assertions.Expect(Page.Locator("#assistant-budget-steps-limit")).ToHaveTextAsync("2", new() { Timeout = 60_000 });
    }

    [Test]
    public async Task AQuestionNobodyAnswersOutlivesAReloadAsync() {
        await SignInAsync();
        long id = await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call-read", "read_page", """{"path":"/a-page-that-is-not-there"}"""),
            StubResponse.Words("The page is missing.", TimeSpan.Zero));

        await SendAsync("Read the missing page");
        await Assertions.Expect(Page.Locator("section.assistant-continuation")).ToHaveCountAsync(1, new() { Timeout = 60_000 });

        await Page.GotoAsync($"assistant/{id}");

        await Assertions.Expect(Page.Locator("section.assistant-continuation button")).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("section.assistant-continuation")).ToContainTextAsync("used all 1 of its steps");
    }
}
