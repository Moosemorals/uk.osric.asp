// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Agent;
using uk.osric.asp.Web.Agent;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AgentRunProcessorTests {

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _databaseOptions = new DbContextOptionsBuilder<StorageDbContext>().Options;
    private ServiceProvider? _services;
    private PageHarness? _pages;
    private readonly AgentOptions _options = new() {
        RunTimeoutSeconds = 5,
        RequestTimeoutSeconds = 3,
        ToolTimeoutSeconds = 2,
        MaxContextChars = 20_000,
    };

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"agent-run-{Guid.NewGuid():N}.db");
        SqliteConnectionStringBuilder connection = new() { DataSource = _databasePath, Pooling = false };
        _databaseOptions = new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(connection.ToString()).Options;
        await using (StorageDbContext database = new(_databaseOptions)) {
            await database.Database.MigrateAsync();
        }

        _pages = new PageHarness(new FakePageStore());
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection.ToString()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAgentConversationStore, SqliteAgentConversationStore>();
        services.AddScoped(_ => _pages.Editor);
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() {
        _services?.Dispose();
        _pages?.Dispose();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    public async Task StreamsAndPersistsACompleteTextTurnAsync() {
        ScriptedClient client = new([
            new AgentContentDelta("Hello "),
            new AgentReasoningDelta("provider reasoning"),
            new AgentContentDelta("there."),
            new AgentFinished("stop"),
        ]);
        (AgentConversation conversation, AgentRunStartResult started, AgentRunStream stream, AgentRunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new AgentQueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IAgentConversationStore store = scope.ServiceProvider.GetRequiredService<IAgentConversationStore>();
        AgentRun run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<AgentRun>().SingleAsync();
        IReadOnlyList<AgentMessage> messages = await store.GetMessagesAsync(conversation.Id, "owner");
        AgentEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("completed"));
            Assert.That(run.StepCount, Is.EqualTo(1));
            Assert.That(messages.Select(message => message.Role), Is.EqualTo(["user", "assistant"]));
            Assert.That(messages[1].Content, Is.EqualTo("Hello there."));
            Assert.That(messages[1].Reasoning, Is.EqualTo("provider reasoning"));
            Assert.That(events.Select(item => item.Type), Does.Contain("content.delta"));
            Assert.That(events[^1].Type, Is.EqualTo("run.completed"));
        });
    }

    [Test]
    public async Task ProviderFailurePersistsPartialAssistantTextAndSafeFailureAsync() {
        ScriptedClient client = new([
            new AgentContentDelta("Partial answer"),
        ], new AgentProviderException("provider_unreachable", "The configured provider could not be reached."));
        (AgentConversation conversation, AgentRunStartResult started, AgentRunStream stream, AgentRunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new AgentQueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        AgentRun run = await database.Set<AgentRun>().SingleAsync();
        AgentMessage assistant = await database.Set<AgentMessage>().SingleAsync(message => message.Role == "assistant");
        AgentEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("failed"));
            Assert.That(run.ErrorCode, Is.EqualTo("provider_unreachable"));
            Assert.That(assistant.Content, Is.EqualTo("Partial answer"));
            Assert.That(assistant.Status, Is.EqualTo("failed"));
            Assert.That(events[^1].Type, Is.EqualTo("run.failed"));
        });
    }

    private async Task<(AgentConversation Conversation, AgentRunStartResult Started, AgentRunStream Stream, AgentRunProcessor Processor)> StartRunAsync(
        IChatCompletionClient client) {
        ServiceProvider services = _services!;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAgentConversationStore store = scope.ServiceProvider.GetRequiredService<IAgentConversationStore>();
        AgentConversation conversation = await store.CreateAsync("owner");
        AgentRunStartResult started = await store.StartRunAsync(conversation.Id, "owner", "Say hello", "test-model", PageAuthorPrompt.Version);
        AgentRunRegistry registry = new(_options, TimeProvider.System);
        Assert.That(registry.TryStart(started.RunId!, conversation.Id, out AgentRunStream? stream), Is.True);
        AgentPaths paths = new(new ReservedPaths(ReservedPaths.Default), _options);
        AgentPageToolExecutor tools = new(services.GetRequiredService<IServiceScopeFactory>(), paths, _options);
        AgentRunProcessor processor = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            tools,
            registry,
            new AgentToolApprovalCoordinator(),
            new AgentMetrics(),
            _options,
            NullLogger<AgentRunProcessor>.Instance);
        return (conversation, started, stream!, processor);
    }

    private static async Task<AgentEvent[]> CollectAsync(AgentRunStream stream) {
        List<AgentEvent> events = [];
        await foreach (AgentEvent @event in stream.ReadAfterAsync(0, CancellationToken.None)) {
            events.Add(@event);
        }
        return [.. events];
    }

    private sealed class ScriptedClient(
        IReadOnlyList<AgentChatStreamEvent> events,
        Exception? error = null) : IChatCompletionClient {

        public async IAsyncEnumerable<AgentChatStreamEvent> StreamAsync(
            AgentChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            foreach (AgentChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
            if (error is not null) {
                throw error;
            }
        }
    }
}
