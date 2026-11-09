// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// What happens when a model thinks for a long time. A step that keeps streaming has to be allowed to
/// finish however long it takes; a step that goes silent has to stop, and the page has to say so.
/// </summary>
[TestFixture]
internal sealed class AssistantCutOffTests : BrowserTest {

    private BrowserHost _host = null!;

    protected override BrowserHost Host => _host;

    [OneTimeSetUp]
    public async Task StartHostAsync() =>
        _host = await BrowserHost.StartAsync(new BrowserHostSettings {
            RequestTimeoutSeconds = 3,
            RunTimeoutSeconds = 120,
            HeartbeatSeconds = 1,
        });

    [OneTimeTearDown]
    public async Task StopHostAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AThinkThatKeepsStreamingIsNotCutOffByTheStepDeadlineAsync() {
        string answer = "The reef is older than the rainforest.";
        await SignInAsync();
        await NewConversationAsync();
        Provider.Script(StubResponse.Thinking(string.Join(' ', Enumerable.Repeat("thinking", 30)), answer, TimeSpan.FromMilliseconds(300)));

        await SendAsync("Think for a long time before answering");
        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync(answer, new() { Timeout = 60_000 });
    }

    [Test]
    public async Task AStepThatGoesSilentStopsAndTheTranscriptSaysWhyAsync() {
        await SignInAsync();
        await NewConversationAsync();
        Provider.Script(new StubResponse {
            Chunks = [
                new StubChunk(Content: "The answer starts "),
                new StubChunk(Content: "and then nothing ", Delay: TimeSpan.FromSeconds(10)),
            ],
        });

        await SendAsync("Start an answer, then go quiet");
        await Assertions.Expect(Page.Locator(".assistant-run-error"))
            .ToContainTextAsync("stopped sending", new() { Timeout = 60_000 });

        string transcript = await Page.Locator("#assistant-transcript").InnerTextAsync();
        Assert.That(transcript, Does.Contain("The answer starts"), "the partial answer is kept");
    }
}
