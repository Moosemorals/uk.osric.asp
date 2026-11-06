// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AssistantRunProcessorTests {

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _databaseOptions = new DbContextOptionsBuilder<StorageDbContext>().Options;
    private ServiceProvider? _services;
    private PageHarness? _pages;
    private readonly AssistantOptions _options = new() {
        RunTimeoutSeconds = 5,
        RequestTimeoutSeconds = 3,
        ToolTimeoutSeconds = 2,
        MaxContextChars = 20_000,
    };

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"assistant-run-{Guid.NewGuid():N}.db");
        SqliteConnectionStringBuilder connection = new() { DataSource = _databasePath, Pooling = false };
        _databaseOptions = new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(connection.ToString()).Options;
        await using (StorageDbContext database = new(_databaseOptions)) {
            await database.Database.MigrateAsync();
        }

        _pages = new PageHarness(new FakePageStore());
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection.ToString()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAssistantConversationStore, SqliteAssistantConversationStore>();
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
            new AssistantContentDelta("Hello "),
            new AssistantReasoningDelta("provider reasoning"),
            new AssistantContentDelta("there."),
            new AssistantFinished("stop"),
        ]);
        (AssistantConversation conversation, AssistantRunStartResult started, AssistantRunStream stream, AssistantRunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new AssistantQueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IAssistantConversationStore store = scope.ServiceProvider.GetRequiredService<IAssistantConversationStore>();
        AssistantRun run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<AssistantRun>().SingleAsync();
        IReadOnlyList<AssistantMessage> messages = await store.GetMessagesAsync(conversation.Id, "owner");
        AssistantEvent[] events = await CollectAsync(stream);

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
    public async Task AToolNameTheProviderInventedBecomesOneMetricLabelAsync() {
        ScriptedClient client = new([
            new AssistantToolCallDelta(0, "call-1", "make_up_a_tool", "{}"),
            new AssistantFinished("tool_calls"),
        ]);
        using MetricCapture capture = MetricCapture.Start(AssistantMetrics.MeterName);
        (AssistantConversation conversation, AssistantRunStartResult started, AssistantRunStream stream, AssistantRunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new AssistantQueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capture.Count("assistant.tool.duration", "tool", "make_up_a_tool"), Is.Zero);
            Assert.That(capture.Count("assistant.tool.duration", "tool", AssistantMetrics.UnknownTool), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ProviderFailurePersistsPartialAssistantTextAndSafeFailureAsync() {
        ScriptedClient client = new([
            new AssistantContentDelta("Partial answer"),
        ], new AssistantProviderException("provider_unreachable", "The configured provider could not be reached."));
        (AssistantConversation conversation, AssistantRunStartResult started, AssistantRunStream stream, AssistantRunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new AssistantQueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        AssistantRun run = await database.Set<AssistantRun>().SingleAsync();
        AssistantMessage assistant = await database.Set<AssistantMessage>().SingleAsync(message => message.Role == "assistant");
        AssistantEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("failed"));
            Assert.That(run.ErrorCode, Is.EqualTo("provider_unreachable"));
            Assert.That(assistant.Content, Is.EqualTo("Partial answer"));
            Assert.That(assistant.Status, Is.EqualTo("failed"));
            Assert.That(events[^1].Type, Is.EqualTo("run.failed"));
        });
    }

    private async Task<(AssistantConversation Conversation, AssistantRunStartResult Started, AssistantRunStream Stream, AssistantRunProcessor Processor)> StartRunAsync(
        IChatCompletionClient client) {
        ServiceProvider services = _services!;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAssistantConversationStore store = scope.ServiceProvider.GetRequiredService<IAssistantConversationStore>();
        AssistantConversation conversation = await store.CreateAsync("owner");
        AssistantRunStartResult started = await store.StartRunAsync(conversation.Id, "owner", "Say hello", "test-model", PageAuthorPrompt.Version);
        AssistantRunRegistry registry = new(_options, TimeProvider.System);
        Assert.That(registry.TryStart(started.RunId!, conversation.Id, out AssistantRunStream? stream), Is.True);
        AssistantPaths paths = new(new ReservedPaths(ReservedPaths.Default), _options);
        AssistantPageToolExecutor tools = new(services.GetRequiredService<IServiceScopeFactory>(), paths, _options);
        AssistantRunProcessor processor = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            tools,
            registry,
            new AssistantToolApprovalCoordinator(),
            new AssistantMetrics(),
            _options,
            NullLogger<AssistantRunProcessor>.Instance);
        return (conversation, started, stream!, processor);
    }

    private static async Task<AssistantEvent[]> CollectAsync(AssistantRunStream stream) {
        List<AssistantEvent> events = [];
        await foreach (AssistantEvent @event in stream.ReadAfterAsync(0, CancellationToken.None)) {
            events.Add(@event);
        }
        return [.. events];
    }

    private sealed class ScriptedClient(
        IReadOnlyList<AssistantChatStreamEvent> events,
        Exception? error = null) : IChatCompletionClient {

        public async IAsyncEnumerable<AssistantChatStreamEvent> StreamAsync(
            AssistantChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            foreach (AssistantChatStreamEvent @event in events) {
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
