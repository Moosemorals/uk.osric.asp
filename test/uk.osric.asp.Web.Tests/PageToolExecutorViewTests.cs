// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageToolExecutorViewTests {

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    [Test]
    public async Task AViewResolvesAPathAndCarriesEveryResponseHeaderAsync() {
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => Response(
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["Content-Type"] = "text/html; charset=utf-8",
                    ["Content-Security-Policy"] = "default-src 'none'",
                    ["Set-Cookie"] = "s=abc",
                },
                body: "<h1>Hello</h1>"),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/hello\"}", Context, CancellationToken.None);

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("path").GetString(), Is.EqualTo("/hello"));
            Assert.That(data.GetProperty("status").GetInt32(), Is.EqualTo(200));
            Assert.That(data.GetProperty("contentType").GetString(), Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(data.GetProperty("headers").TryGetProperty("Content-Security-Policy", out _), Is.True);
            Assert.That(data.GetProperty("headers").TryGetProperty("Set-Cookie", out _), Is.True, "a view is the response, cookies included");
            Assert.That(data.GetProperty("content").GetString(), Is.EqualTo("<h1>Hello</h1>"));
            Assert.That(data.GetProperty("length").GetInt32(), Is.EqualTo(Encoding.UTF8.GetByteCount("<h1>Hello</h1>")));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(fetcher.Viewed, Is.EqualTo(["http://127.0.0.1:8080/hello"]));
        });
    }

    [Test]
    public async Task AViewPresentsTheRunsOwnTokenAsync() {
        FakeAssistantFetcher fetcher = new();
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        _ = await tools.ExecuteAsync("view", "{\"path\":\"/\"}", Context, CancellationToken.None);

        Assert.That(fetcher.ViewTokens, Has.Count.EqualTo(1));
        Assert.That(fetcher.ViewTokens[0]!.RunId, Is.EqualTo("run-1"));
        Assert.That(fetcher.ViewTokens[0]!.Owner, Is.EqualTo("owner"));
    }

    [Test]
    public async Task AViewWithNoTokenIsRefusedRatherThanRenderingAsAStrangerAsync() {
        FakeAssistantFetcher fetcher = new();
        PageToolExecutor tools = CreateTools(fetcher, new ViewTokenStore(TestOptions.Assistant));

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/hello\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("view token"));
            Assert.That(fetcher.Viewed, Is.Empty);
        });
    }

    [TestCase("https://example.com/x")]
    [TestCase("//example.com/x")]
    [TestCase("hello")]
    public async Task AViewRefusesAnythingThatIsNotAPathOnThisSiteAsync(string path) {
        FakeAssistantFetcher fetcher = new();
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", JsonSerializer.Serialize(new { path }), Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("fetch"));
            Assert.That(fetcher.Viewed, Is.Empty);
        });
    }

    [Test]
    public async Task ABodyThatIsNotUtf8ComesBackAsBase64RatherThanReplacementCharactersAsync() {
        byte[] binary = [0xFF, 0xFE, 0x00];
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => new OutboundResponse(
                200,
                new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" },
                binary,
                HttpFetchFailure.None,
                false,
                []),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/blob\"}", Context, CancellationToken.None);

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(data.GetProperty("validUtf8").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("content").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Convert.FromBase64String(data.GetProperty("base64").GetString()!), Is.EqualTo(binary));
        });
    }

    [Test]
    public async Task ABodyOverTheBudgetIsCutAndSaysSo() {
        string big = new('x', 6000);
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => Response(body: big),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken(), maxToolResultChars: 4000);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/big\"}", Context, CancellationToken.None);

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("length").GetInt32(), Is.EqualTo(6000), "the full size is still reported");
            Assert.That(data.GetProperty("content").GetString()!.Length, Is.EqualTo(2000));
        });
    }

    [Test]
    public async Task AViewReportsTheRedirectItFollowedAsync() {
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => new OutboundResponse(
                200,
                new Dictionary<string, string> { ["Content-Type"] = "text/html" },
                Encoding.UTF8.GetBytes("<form>sign in</form>"),
                HttpFetchFailure.None,
                false,
                [new OutboundRedirect(302, "http://127.0.0.1:8080/login?returnUrl=/pages")]),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/pages\"}", Context, CancellationToken.None);

        JsonElement redirects = Data(result).GetProperty("redirects");
        Assert.Multiple(() => {
            Assert.That(redirects.GetArrayLength(), Is.EqualTo(1));
            Assert.That(redirects[0].GetProperty("status").GetInt32(), Is.EqualTo(302));
            Assert.That(redirects[0].GetProperty("location").GetString(), Does.Contain("/login"));
        });
    }

    [Test]
    public async Task AViewRefusesANetworkFailureAsync() {
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => new OutboundResponse(0, new Dictionary<string, string>(), [], HttpFetchFailure.Network, false, []),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/hello\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("Site:Origin"));
            Assert.That(result.Summary, Is.EqualTo("View failed: network"));
        });
    }

    [Test]
    public async Task A4xxIsDataRatherThanAToolFailureAsync() {
        FakeAssistantFetcher fetcher = new() {
            ViewResponse = _ => new OutboundResponse(
                404,
                new Dictionary<string, string> { ["Content-Type"] = "text/html" },
                Encoding.UTF8.GetBytes("no such page"),
                HttpFetchFailure.HttpError,
                false,
                []),
        };
        PageToolExecutor tools = CreateTools(fetcher, WithToken());

        ToolExecutionResult result = await tools.ExecuteAsync(
            "view", "{\"path\":\"/missing\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("status").GetInt32(), Is.EqualTo(404));
        });
    }

    private static ViewTokenStore WithToken() {
        ViewTokenStore store = new(TestOptions.Assistant);
        _ = store.Mint("run-1", "owner", "stamp");

        return store;
    }

    private static PageToolExecutor CreateTools(
        FakeAssistantFetcher fetcher,
        ViewTokenStore store,
        int maxToolResultChars = 4096) {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(TestOptions.Editor)
            .AddSingleton(store)
            .AddSingleton(TestOptions.Site)
            .BuildServiceProvider();

        return new PageToolExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), TestOptions.Assistant),
            TestOptions.Assistant with { MaxToolArgumentChars = 4096, MaxToolResultChars = maxToolResultChars, MaxPageChars = 4096 },
            new AssistantMetrics(),
            fetcher);
    }

    private static OutboundResponse Response(string body, Dictionary<string, string>? headers = null) =>
        new(
            200,
            headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["Content-Type"] = "text/plain; charset=utf-8",
            },
            Encoding.UTF8.GetBytes(body),
            HttpFetchFailure.None,
            false,
            []);

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }
}
