// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// The same conversation page against the provider the site is really configured for. It is explicit
/// because it spends real tokens and needs a key, and it is the only test here that can see what a
/// model actually does when it thinks for a while.
/// </summary>
[TestFixture]
[Explicit("Calls the configured provider; run `make test-browser-live`.")]
internal sealed class AssistantLiveProviderTests : BrowserTest {

    private const string DefaultModel = "deepseek-reasoner";

    private BrowserHost _host = null!;

    protected override BrowserHost Host => _host;

    [OneTimeSetUp]
    public async Task StartHostAsync() {
        string? token = Environment.GetEnvironmentVariable("ASSISTANT_LIVE_TOKEN");
        if (string.IsNullOrWhiteSpace(token)) {
            string tokenFile = Path.Combine(BrowserHost.RepoRoot(), "deploy", "deepseek.token");
            if (!File.Exists(tokenFile)) {
                Assert.Ignore($"No provider key: set ASSISTANT_LIVE_TOKEN or put one in {tokenFile}.");
            }
            token = (await File.ReadAllTextAsync(tokenFile)).Trim();
        }

        _host = await BrowserHost.StartAsync(new BrowserHostSettings {
            ProviderBaseUrl = new Uri(Environment.GetEnvironmentVariable("ASSISTANT_LIVE_BASE_URL") ?? "https://api.deepseek.com"),
            ApiKey = token,
            Model = Environment.GetEnvironmentVariable("ASSISTANT_LIVE_MODEL") ?? DefaultModel,
            RequestTimeoutSeconds = 120,
            RunTimeoutSeconds = 600,
        });
    }

    [OneTimeTearDown]
    public async Task StopHostAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ARealModelAsksAQuestionAndUsesTheAnswerAsync() {
        string first = UniquePath("live-choice-a");
        string second = UniquePath("live-choice-b");
        await SignInAsync();
        await NewConversationAsync();

        await SendAsync(
            $"Use the ask_user tool to ask me which of these two page keys a new page should use: "
            + $"{first} or {second}. Offer exactly those two as options. When I answer, use write_page "
            + $"to create the page I chose, with a paragraph reading 'asked and answered', then reply "
            + $"with one short sentence.");

        await Assertions.Expect(Page.Locator(".assistant-question")).ToBeVisibleAsync(new() { Timeout = 240_000 });
        // The owner's own words are always a way to answer, whatever options the model offered.
        await Page.FillAsync(".assistant-question input[name='other0']", first);
        await Page.ClickAsync(".assistant-question button[type=submit]");

        await Assertions.Expect(Page.Locator("details.assistant-tool")).ToContainTextAsync("write_page", new() { Timeout = 240_000 });
        Assert.That(Host.Log, Does.Not.Contain("finished as failed"));

        IPage visitor = await NewPageAsync();
        IResponse? written = await visitor.GotoAsync(first.TrimStart('/'));
        string content = await visitor.ContentAsync();
        Assert.Multiple(() => {
            Assert.That(written?.Status, Is.EqualTo(200), "the model wrote the page the owner named");
            Assert.That(content, Does.Contain("asked and answered"));
        });
    }

    [Test]
    public async Task ARealModelFinishesAConversationThatNeedsAToolCallAsync() {
        string path = UniquePath("live");
        string phrase = $"live check {Guid.NewGuid():N}";
        await SignInAsync();
        await NewConversationAsync();

        await SendAsync(
            $"Use the write_page tool to create the page {path} whose content is a paragraph reading " +
            $"'{phrase}'. Then reply with one short sentence saying you have done it.");

        await Assertions.Expect(Page.Locator("details.assistant-tool")).ToContainTextAsync("write_page", new() { Timeout = 240_000 });
        await Assertions.Expect(Page.Locator(".assistant-content").Last).Not.ToBeEmptyAsync(new() { Timeout = 240_000 });
        Assert.That(Host.Log, Does.Not.Contain("finished as failed"));

        // The run's completion reloads the conversation page, and that reload would abort a
        // navigation started from it; the written page is read from a page of its own.
        IPage visitor = await NewPageAsync();
        IResponse? written = await visitor.GotoAsync(path.TrimStart('/'));
        string content = await visitor.ContentAsync();
        Assert.Multiple(() => {
            Assert.That(written?.Status, Is.EqualTo(200));
            Assert.That(content, Does.Contain(phrase));
        });
    }
}
