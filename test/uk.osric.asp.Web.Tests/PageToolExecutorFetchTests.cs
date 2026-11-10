// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageToolExecutorFetchTests {

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    [Test]
    public async Task FetchReturnsTheStatusTypeAndContentAsync() {
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string> { ["Content-Type"] = "text/html; charset=utf-8" },
            "<h1>Hello</h1>",
            HttpFetchFailure.None,
            false));
        PageToolExecutor tools = CreateTools(fetcher);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("url").GetString(), Is.EqualTo("https://example.com/"));
            Assert.That(Data(result).GetProperty("status").GetInt32(), Is.EqualTo(200));
            Assert.That(Data(result).GetProperty("contentType").GetString(), Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(Data(result).GetProperty("content").GetString(), Is.EqualTo("<h1>Hello</h1>"));
            Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(fetcher.Requests, Is.EqualTo(["https://example.com/"]));
        });
    }

    [Test]
    public async Task FetchReturnsA4xxAsDataRatherThanAToolFailureAsync() {
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            404,
            new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            "missing",
            HttpFetchFailure.HttpError,
            false));
        PageToolExecutor tools = CreateTools(fetcher);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/missing\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(Data(result).GetProperty("content").GetString(), Is.EqualTo("missing"));
        });
    }

    [Test]
    public async Task FetchRefusesANetworkFailureAsync() {
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            HttpFetchFailure.Network,
            false));
        PageToolExecutor tools = CreateTools(fetcher);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("could not be reached"));
            Assert.That(result.Summary, Is.EqualTo("Fetch failed: network"));
        });
    }

    [Test]
    public async Task FetchRefusesAnInvalidUrlAsync() {
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            HttpFetchFailure.InvalidRequest,
            false));
        PageToolExecutor tools = CreateTools(fetcher);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"ftp://example.com/\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("absolute http or https"));
            Assert.That(result.Summary, Is.EqualTo("Fetch refused"));
        });
    }

    [Test]
    public async Task FetchTruncatesALongBodyToHalfTheResultBudgetAsync() {
        string body = new('x', 10_000);
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
            body,
            HttpFetchFailure.None,
            false));
        PageToolExecutor tools = CreateTools(fetcher, new AssistantOptions {
            MaxToolArgumentChars = 4096,
            MaxToolResultChars = 4096,
            MaxPageChars = 4096,
        });

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("content").GetString(), Has.Length.EqualTo(2048));
            Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task FetchCarriesTheTransportTruncationFlagAsync() {
        FakeAssistantFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string>(),
            "cut",
            HttpFetchFailure.None,
            true));
        PageToolExecutor tools = CreateTools(fetcher);

        ToolExecutionResult result = await tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(Data(result).GetProperty("contentType").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Data(result).GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task FetchPassesACancellationThroughToTheRunAsync() {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        PageToolExecutor tools = CreateTools(new FakeAssistantFetcher());

        await Assert.ThrowsAsync<OperationCanceledException>(() => tools.ExecuteAsync(
            "fetch", "{\"url\":\"https://example.com/\"}", Context, cancelled.Token));
    }

    [Test]
    public void FetchIsApprovalGatedAndTakesOnlyAUrl() {
        ToolDefinition definition = PageToolExecutor.Definitions.Single(item => item.Name == "fetch");
        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);

        Assert.Multiple(() => {
            Assert.That(definition.NeedsApproval, Is.True);
            Assert.That(parameters.RootElement.GetProperty("properties").TryGetProperty("url", out _), Is.True);
            Assert.That(parameters.RootElement.GetProperty("properties").EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(parameters.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo<string[]>(["url"]));
        });
    }

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static PageToolExecutor CreateTools(FakeAssistantFetcher fetcher, AssistantOptions? options = null) {
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        return new PageToolExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), new AssistantOptions()),
            options ?? new AssistantOptions { MaxToolArgumentChars = 4096, MaxToolResultChars = 4096, MaxPageChars = 4096 },
            new AssistantMetrics(),
            fetcher);
    }
}
