// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// The stream over a slow model: answers that arrive for longer than a heartbeat, answers that keep
/// flowing while the page is reloaded, and a provider that goes quiet in the middle of one.
/// </summary>
[TestFixture]
internal sealed class AssistantStreamTests : BrowserTest {

    [Test]
    public async Task EveryChunkOfAnAnswerSlowerThanAHeartbeatReachesTheBrowserAsync() {
        string answer = string.Join(' ', Enumerable.Range(1, 120).Select(index => $"part{index}"));
        await NewConversationAsync();
        Provider.Script(StubResponse.Words(answer, TimeSpan.FromMilliseconds(80)));

        await SendAsync("Answer slowly");
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("part120", new() { Timeout = 60_000 });

        string transcript = await Page.Locator("#assistant-transcript").InnerTextAsync();
        Assert.That(transcript, Does.Contain("part1 ").And.Contain("part60 ").And.Contain("part120"));
    }

    [Test]
    public async Task AReloadDuringARunPicksTheAnswerBackUpAsync() {
        string answer = string.Join(' ', Enumerable.Range(1, 100).Select(index => $"chunk{index}"));
        long id = await NewConversationAsync();
        Provider.Script(StubResponse.Words(answer, TimeSpan.FromMilliseconds(120)));

        await SendAsync("Answer across a reload");
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("chunk10 ", new() { Timeout = 30_000 });
        await Page.GotoAsync($"assistant/{id}");

        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("chunk100", new() { Timeout = 60_000 });
    }

    [Test]
    public async Task AProviderThatGoesQuietForAWhileStillCompletesAsync() {
        await NewConversationAsync();
        StubResponse slow = StubResponse.Words("It finished after the pause.", TimeSpan.Zero);
        Provider.Script(new StubResponse { Delay = TimeSpan.FromSeconds(6), Chunks = slow.Chunks });

        await SendAsync("Take your time");
        await Assertions.Expect(Page.Locator("#assistant-transcript"))
            .ToContainTextAsync("It finished after the pause.", new() { Timeout = 60_000 });
    }

    [Test]
    public async Task AToolCallAndItsAnswerBothAppearAsync() {
        string path = UniquePath("written-by-the-assistant");
        await NewConversationAsync();
        Provider.Script(
            StubResponse.Tool("call_1", "write_page", $$"""{"path":"{{path}}","content":"<% ctx.Capabilities.GetRequired<IPageLayout>().Title = \"Written by the stub\"; %><h1>Written by the stub</h1><p>written by the stub</p>"}"""),
            StubResponse.Words("I wrote the page.", TimeSpan.Zero));

        await SendAsync($"Write {path}");
        await Assertions.Expect(Page.Locator("details.assistant-tool")).ToContainTextAsync("write_page", new() { Timeout = 60_000 });
        await Assertions.Expect(Page.Locator("#assistant-transcript")).ToContainTextAsync("I wrote the page.", new() { Timeout = 60_000 });

        IResponse? written = await Page.GotoAsync(path.TrimStart('/'));
        Assert.That(written?.Status, Is.EqualTo(200));
    }
}
