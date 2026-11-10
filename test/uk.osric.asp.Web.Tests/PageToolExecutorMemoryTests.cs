// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageToolExecutorMemoryTests {

    private static DatabaseTemplate? s_template;

    private readonly AssistantOptions _options = new() {
        MaxToolArgumentChars = 4_096,
        MaxToolResultChars = 4_096,
        MaxPageChars = 4_096,
    };

    private string _databasePath = string.Empty;
    private ServiceProvider _services = null!;

    private static readonly ToolContext Context = new("owner", ConversationId: 7, RunId: "run-1");

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("assistant-memory-tools");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void SetUp() {
        _databasePath = s_template!.CreateFile("assistant-memory-tools");
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(builder =>
            builder.UseSqlite(DatabaseTemplate.ConnectionString(_databasePath)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_options);
        services.AddSingleton(new AssistantMetrics());
        services.AddScoped<IMemoryStore, SqliteMemoryStore>();
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() {
        _services.Dispose();
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task RememberValidatesItsArgumentsBeforeItTouchesTheStoreAsync() {
        PageToolExecutor tools = CreateTools();

        ToolExecutionResult missing = await tools.ExecuteAsync("remember", "{\"kind\":\"fact\"}", Context, CancellationToken.None);
        ToolExecutionResult extra = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"fact\",\"content\":\"x\",\"owner\":\"somebody\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(missing.IsSuccess, Is.False);
            Assert.That(missing.Content, Does.Contain("Required tool argument").And.Contain("is missing"));
            Assert.That(extra.IsSuccess, Is.False);
            Assert.That(extra.Content, Does.Contain("Unknown tool argument").And.Contain("owner"));
            Assert.That(Count(), Is.Zero);
        });
    }

    [Test]
    public async Task ABadKindIsARefusalThatNamesTheFourKindsAsync() {
        PageToolExecutor tools = CreateTools();

        ToolExecutionResult result = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"opinion\",\"content\":\"The owner likes short answers.\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("preference"));
            Assert.That(result.Content, Does.Contain("reference"));
            Assert.That(Count(), Is.Zero);
        });
    }

    [Test]
    public async Task RememberWritesUnderTheContextsOwnerAndNeverTheArgumentsAsync() {
        PageToolExecutor tools = CreateTools();

        ToolExecutionResult stored = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"preference\",\"content\":\"The owner writes British English.\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(stored.IsSuccess, Is.True, stored.Content);
            Assert.That(stored.Summary, Is.EqualTo("Remembered a preference"));
            Assert.That(Data(stored).GetProperty("kind").GetString(), Is.EqualTo("preference"));
            Assert.That(Data(stored).GetProperty("version").GetInt32(), Is.EqualTo(1));
            Assert.That(Owners(), Is.EqualTo(["owner"]));
        });
    }

    [Test]
    public async Task SearchRespectsItsLimitAndTheConfiguredMaximumAsync() {
        PageToolExecutor tools = CreateTools();
        foreach (string note in new[] {
            "The deploy script runs in the container.",
            "The deploy pipeline needs a token from the vault.",
            "A deploy is a restart of the unit.",
        }) {
            ToolExecutionResult stored = await tools.ExecuteAsync(
                "remember", JsonSerializer.Serialize(new { kind = "fact", content = note }), Context, CancellationToken.None);
            Assert.That(stored.IsSuccess, Is.True, stored.Content);
        }

        ToolExecutionResult limited = await tools.ExecuteAsync(
            "search_memories", "{\"query\":\"deploy\",\"limit\":2}", Context, CancellationToken.None);
        ToolExecutionResult unbounded = await tools.ExecuteAsync(
            "search_memories", "{\"query\":\"deploy\"}", Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(limited.IsSuccess, Is.True, limited.Content);
            Assert.That(Data(limited).GetProperty("memories").GetArrayLength(), Is.EqualTo(2));
            Assert.That(Data(unbounded).GetProperty("memories").GetArrayLength(), Is.EqualTo(3));
            Assert.That(Data(unbounded).GetProperty("memories")[0].GetProperty("id").GetInt64(), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task AnotherOwnersMemoryIsNotThereToUpdateOrForgetAsync() {
        PageToolExecutor tools = CreateTools();
        ToolExecutionResult stored = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"fact\",\"content\":\"The site runs on Podman.\"}", Context, CancellationToken.None);
        long id = Data(stored).GetProperty("id").GetInt64();
        ToolContext other = new("other", ConversationId: 8, RunId: "run-2");

        ToolExecutionResult updated = await tools.ExecuteAsync(
            "update_memory", JsonSerializer.Serialize(new { id, expectedVersion = 1, content = "Changed." }), other, CancellationToken.None);
        ToolExecutionResult forgotten = await tools.ExecuteAsync(
            "forget_memory", JsonSerializer.Serialize(new { id }), other, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(updated.IsSuccess, Is.False);
            Assert.That(updated.Content, Does.Contain($"No memory with id {id}."));
            Assert.That(forgotten.IsSuccess, Is.False);
            Assert.That(Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AStaleUpdateIsAResultCarryingTheCurrentContentAsync() {
        PageToolExecutor tools = CreateTools();
        ToolExecutionResult stored = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"preference\",\"content\":\"Prefers colour.\"}", Context, CancellationToken.None);
        long id = Data(stored).GetProperty("id").GetInt64();
        _ = await tools.ExecuteAsync(
            "update_memory", JsonSerializer.Serialize(new { id, expectedVersion = 1, content = "Prefers colour, always." }), Context, CancellationToken.None);

        ToolExecutionResult stale = await tools.ExecuteAsync(
            "update_memory", JsonSerializer.Serialize(new { id, expectedVersion = 1, content = "Prefers color." }), Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(stale.IsSuccess, Is.False);
            Assert.That(stale.Content, Does.Contain("Prefers colour, always."));
            Assert.That(stale.Summary, Is.EqualTo("Update memory refused"));
        });
    }

    [Test]
    public async Task ARefusalIsAToolResultRatherThanAnExceptionAsync() {
        PageToolExecutor tools = CreateTools();

        ToolExecutionResult refused = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"reference\",\"content\":\"api_key=7f3c9a1b2e\"}", Context, CancellationToken.None);

        using JsonDocument envelope = JsonDocument.Parse(refused.Content);
        Assert.Multiple(() => {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(envelope.RootElement.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(envelope.RootElement.GetProperty("error").GetString(), Does.Contain("refused"));
            Assert.That(Count(), Is.Zero);
        });
    }

    [Test]
    public async Task ForgetRemovesTheMemoryAndSaysSoAsync() {
        PageToolExecutor tools = CreateTools();
        ToolExecutionResult stored = await tools.ExecuteAsync(
            "remember", "{\"kind\":\"fact\",\"content\":\"The site runs on Podman.\"}", Context, CancellationToken.None);
        long id = Data(stored).GetProperty("id").GetInt64();

        ToolExecutionResult forgotten = await tools.ExecuteAsync(
            "forget_memory", JsonSerializer.Serialize(new { id }), Context, CancellationToken.None);
        ToolExecutionResult again = await tools.ExecuteAsync(
            "forget_memory", JsonSerializer.Serialize(new { id }), Context, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(forgotten.IsSuccess, Is.True, forgotten.Content);
            Assert.That(forgotten.Summary, Is.EqualTo($"Forgot memory {id}"));
            Assert.That(again.IsSuccess, Is.False);
            Assert.That(Count(), Is.Zero);
        });
    }

    [Test]
    public void TheWriteToolsNeedApprovalAndTheSearchDoesNot() {
        Assert.Multiple(() => {
            Assert.That(Tool("remember").NeedsApproval, Is.True);
            Assert.That(Tool("update_memory").NeedsApproval, Is.True);
            Assert.That(Tool("forget_memory").NeedsApproval, Is.True);
            Assert.That(Tool("search_memories").NeedsApproval, Is.False);
        });
    }

    private static ToolDefinition Tool(string name) =>
        PageToolExecutor.Definitions.Single(definition => definition.Name == name);

    private PageToolExecutor CreateTools() => new(
        _services.GetRequiredService<IServiceScopeFactory>(),
        new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), _options),
        _options,
        _services.GetRequiredService<AssistantMetrics>(),
        new FakeAssistantFetcher());

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }

    private int Count() {
        using IServiceScope scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMemoryStore>().CountAsync("owner").GetAwaiter().GetResult();
    }

    private string[] Owners() {
        using IServiceScope scope = _services.CreateScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        return [.. database.Set<MemoryEntry>().Select(memory => memory.Owner).Distinct().ToArray()];
    }
}
