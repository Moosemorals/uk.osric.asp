// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the host wires a page's fetch through the configured fetcher.</summary>
[TestFixture]
public sealed class PageFetchTests : IntegrationTest {

    private readonly RecordingPageFetcher _fetcher = new();

    internal override AspApplication CreateApplication() {
        return new AspApplication(pageFetcher: _fetcher);
    }

    // The host is one application per fixture, so the fetcher is shared; every test starts clean.
    [SetUp]
    public void ResetFetcher() => _fetcher.Reset();

    [Test]
    public async Task AStoredPageFetchesThroughTheHostAsync() {
        await SavePageAsync("/live", """
            <% PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/state")); %>
            <p>state: <%= response.Body %></p>
            """);
        using HttpClient client = CreateClient();

        string body = await client.GetStringAsync("/live");

        Assert.That(body, Does.Contain("state: up"));
        Assert.That(_fetcher.Requests.Single().Url, Is.EqualTo("https://api.example.com/state"));
    }

    [Test]
    public async Task ThePageKeyTravelsWithTheRequestAsync() {
        await SavePageAsync("/live", """
            <% await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/state")); %>ok
            """);
        using HttpClient client = CreateClient();

        await client.GetStringAsync("/live");

        Assert.That(_fetcher.LastPagePath, Is.EqualTo("/live"));
    }

    [Test]
    public async Task AFetchRefusalRendersTheFailureAsync() {
        await SavePageAsync("/live", """
            <% PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/state")); %>
            <p><%= response.Failure.ToString() %></p>
            """);
        _fetcher.Answer = _ => new PageFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            PageFetchFailure.NotAllowed,
            false);
        using HttpClient client = CreateClient();

        Assert.That(await client.GetStringAsync("/live"), Does.Contain("NotAllowed"));
    }

    private sealed class RecordingPageFetcher : IPageFetcher {
        public List<PageFetchRequest> Requests { get; } = [];

        public string? LastPagePath { get; private set; }

        public Func<PageFetchRequest, PageFetchResponse>? Answer { get; set; }

        public void Reset() {
            Requests.Clear();
            Answer = null;
            LastPagePath = null;
        }

        public Task<PageFetchResponse> FetchAsync(
            PageFetchRequest request,
            string pagePath,
            CancellationToken aborted,
            CancellationToken deadline) {
            Requests.Add(request);
            LastPagePath = pagePath;

            PageFetchResponse response = Answer?.Invoke(request) ?? new PageFetchResponse(
                200,
                new Dictionary<string, string>(),
                "up",
                PageFetchFailure.None,
                false);

            return Task.FromResult(response);
        }
    }
}
