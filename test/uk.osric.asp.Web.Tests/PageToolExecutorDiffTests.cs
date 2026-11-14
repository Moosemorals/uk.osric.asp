// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageToolExecutorDiffTests {

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    [Test]
    public void TheDefinitionsOfferDiffPageAsAReadOnlyTool() {
        ToolDefinition definition = PageToolExecutor.Definitions.Single(item => item.Name == "diff_page");

        using JsonDocument parameters = JsonDocument.Parse(definition.ParametersJson);
        JsonElement properties = parameters.RootElement.GetProperty("properties");

        Assert.Multiple(() => {
            Assert.That(definition.NeedsApproval, Is.False);
            Assert.That(properties.TryGetProperty("path", out _), Is.True);
            Assert.That(properties.TryGetProperty("fromHash", out _), Is.True);
            Assert.That(properties.TryGetProperty("toHash", out _), Is.True);
            Assert.That(parameters.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
                Is.EqualTo<string[]>(["path"]));
        });
    }

    [Test]
    public async Task DiffPageDefaultsToTheNewestTwoRevisionsAsync() {
        FakePageStore storage = new();
        PageTemplate first = new(PagePath.Normalize("/notes"), "one\nfirst");
        PageTemplate second = new(PagePath.Normalize("/notes"), "one\nsecond");
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        ToolExecutionResult result = await RunAsync(storage, """{"path":"/notes"}""");

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("changed").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("from").GetProperty("hash").GetString(),
                Is.AnyOf(first.Hash, second.Hash));
            Assert.That(data.GetProperty("to").GetProperty("hash").GetString(),
                Is.AnyOf(first.Hash, second.Hash));
            Assert.That(data.GetProperty("from").GetProperty("hash").GetString(),
                Is.Not.EqualTo(data.GetProperty("to").GetProperty("hash").GetString()));
            Assert.That(data.GetProperty("diff").GetString(), Does.Contain("-second").Or.Contain("-first"));
            Assert.That(data.GetProperty("editorUrl").GetString(), Does.Contain("cmd=diff"));
        });
    }

    [Test]
    public async Task DiffPageUsesTheExplicitHashesInOrderAsync() {
        FakePageStore storage = new();
        PageTemplate before = new(PagePath.Normalize("/notes"), "one\nbefore");
        PageTemplate after = new(PagePath.Normalize("/notes"), "one\nafter");
        await storage.SavePageAsync(before);
        await storage.SavePageAsync(after);

        ToolExecutionResult result = await RunAsync(
            storage,
            $$"""{"path":"/notes","fromHash":"{{before.Hash}}","toHash":"{{after.Hash}}"}""");

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("from").GetProperty("hash").GetString(), Is.EqualTo(before.Hash));
            Assert.That(data.GetProperty("to").GetProperty("hash").GetString(), Is.EqualTo(after.Hash));
            Assert.That(data.GetProperty("diff").GetString(), Does.Contain("-before").And.Contain("+after"));
        });
    }

    [Test]
    public async Task DiffPageRefusesAHashFromAnotherPageAsync() {
        FakePageStore storage = new();
        PageTemplate mine = new(PagePath.Normalize("/notes"), "mine");
        PageTemplate other = new(PagePath.Normalize("/other"), "other");
        await storage.SavePageAsync(mine);
        await storage.SavePageAsync(other);

        ToolExecutionResult result = await RunAsync(
            storage,
            $$"""{"path":"/notes","fromHash":"{{mine.Hash}}","toHash":"{{other.Hash}}"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("not a revision"));
        });
    }

    [Test]
    public async Task DiffPageRefusesAPageWithOneRevisionAsync() {
        FakePageStore storage = new();
        await storage.SavePageAsync(new PageTemplate(PagePath.Normalize("/notes"), "only"));

        ToolExecutionResult result = await RunAsync(storage, """{"path":"/notes"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("one saved revision"));
        });
    }

    [Test]
    public async Task DiffPageRefusesAShellFileAsync() {
        ToolExecutionResult result = await RunAsync(new FakePageStore(), """{"path":"/admin/scrub"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("shell file"));
        });
    }

    [Test]
    public async Task DiffPageRefusesAMalformedHashAsync() {
        FakePageStore storage = new();
        await storage.SavePageAsync(new PageTemplate(PagePath.Normalize("/notes"), "one"));

        ToolExecutionResult result = await RunAsync(storage, """{"path":"/notes","fromHash":"nope"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("64 lowercase hexadecimal"));
        });
    }

    [Test]
    public async Task DiffPageTruncatesATooLargeDiffAsync() {
        FakePageStore storage = new();
        string before = string.Join('\n', Enumerable.Range(0, 200).Select(index => $"before {index}"));
        string after = string.Join('\n', Enumerable.Range(0, 200).Select(index => $"after {index}"));
        PageTemplate first = new(PagePath.Normalize("/notes"), before);
        PageTemplate second = new(PagePath.Normalize("/notes"), after);
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        ToolExecutionResult result = await RunAsync(storage, """{"path":"/notes"}""", TestOptions.Assistant with {
            MaxToolArgumentChars = 8_192,
            MaxToolResultChars = 2_048,
            MaxPageChars = 8_192,
        });

        JsonElement data = Data(result);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("diff").GetString(), Does.EndWith("[truncated]"));
        });
    }

    private static async Task<ToolExecutionResult> RunAsync(
        FakePageStore storage,
        string arguments,
        AssistantOptions? options = null) {
        using PageHarness pages = new(storage);
        using ServiceProvider services = new ServiceCollection()
            .AddScoped(_ => pages.Editor)
            .AddSingleton(TestOptions.Editor)
            .BuildServiceProvider();

        PageToolExecutor tools = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), TestOptions.Assistant),
            options ?? TestOptions.Assistant with { MaxToolArgumentChars = 8_192, MaxToolResultChars = 8_192, MaxPageChars = 8_192 },
            new AssistantMetrics(),
            new FakeAssistantFetcher(),
            new DiffRenderer());

        return await tools.ExecuteAsync("diff_page", arguments, Context, CancellationToken.None);
    }

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }
}
