// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// A real model with a tiny output budget, which is the other way a long think loses its answer: the
/// reasoning spends the tokens and the provider stops mid-sentence at the limit.
/// </summary>
[TestFixture]
[Explicit("Calls the configured provider; run `make test-browser-live`.")]
internal sealed class AssistantLiveTruncationTests : BrowserTest {

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
            Model = Environment.GetEnvironmentVariable("ASSISTANT_LIVE_MODEL") ?? "deepseek-flash",
            MaxOutputTokens = 96,
        });
    }

    [OneTimeTearDown]
    public async Task StopHostAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AnAnswerStoppedAtTheTokenLimitSaysSoAfterTheReloadAsync() {
        await SignInAsync();
        await Page.GotoAsync("assistant");
        await Page.ClickAsync("text=New conversation");
        await Page.WaitForURLAsync(AssistantUrl());
        await Page.FillAsync("#assistant-message", "Explain, at length, why the sea is salty.");
        await Page.ClickAsync("#assistant-send");

        await Assertions.Expect(Page.Locator(".assistant-incomplete"))
            .ToContainTextAsync("reached the provider's token limit", new() { Timeout = 240_000 });
    }
}
