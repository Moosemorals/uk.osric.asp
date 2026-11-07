// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

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
public sealed class RunProcessorTests {

    private static DatabaseTemplate? s_template;

    private readonly AssistantOptions _options = new() {
        RunTimeoutSeconds = 5,
        RequestTimeoutSeconds = 3,
        ToolTimeoutSeconds = 2,
        MaxContextChars = 20_000,
    };

    private string _databasePath = string.Empty;
    private ServiceProvider? _services;
    private PageHarness? _pages;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("assistant-run");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void SetUp() {
        _databasePath = s_template!.CreateFile("assistant-run");

        _pages = new PageHarness(new FakePageStore());
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(DatabaseTemplate.ConnectionString(_databasePath)));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IConversationStore, SqliteConversationStore>();
        services.AddScoped(_ => _pages.Editor);
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() {
        _services?.Dispose();
        _pages?.Dispose();
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task StreamsAndPersistsACompleteTextTurnAsync() {
        ScriptedClient client = new([
            new ContentDelta("Hello "),
            new ReasoningDelta("provider reasoning"),
            new ContentDelta("there."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<Run>().SingleAsync();
        IReadOnlyList<Message> messages = await store.GetMessagesAsync(conversation.Id, "owner");
        RunEvent[] events = await CollectAsync(stream);

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
    public async Task AMessageStoppedAtTheTokenLimitIsStoredAsTruncatedAsync() {
        ScriptedClient client = new([
            new ContentDelta("Half an ans"),
            new Finished("length"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        IReadOnlyList<Message> messages = await store.GetMessagesAsync(conversation.Id, "owner");
        Assert.That(messages[^1].Status, Is.EqualTo("truncated"));
    }

    [Test]
    public async Task ARunThatKeepsStreamingOutlivesTheRunTimeoutAsync() {
        ScriptedClient client = new(
            [.. Enumerable.Range(1, 6).Select(index => new ContentDelta($"part {index} "))],
            delayMilliseconds: 400);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 1));

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? run = await store.GetLastRunAsync(conversation.Id, "owner");
        Assert.That(run?.Status, Is.EqualTo("completed"));
    }

    [Test]
    public async Task ARunThatStopsStreamingFailsWithRunTimeoutAsync() {
        ScriptedClient client = new([new ContentDelta("too late")], delayMilliseconds: 3_000);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 1));

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? run = await store.GetLastRunAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(run?.Status, Is.EqualTo("failed"));
            Assert.That(run?.ErrorCode, Is.EqualTo("run_timeout"));
        });
    }

    [Test]
    public async Task AProviderThatStopsSendingFailsTheRunWithItsOwnCodeAsync() {
        ScriptedClient client = new([new ContentDelta("Starting")],
            new ProviderException("provider_timeout", "The provider stopped sending before the answer was complete."));
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? run = await store.GetLastRunAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(run?.Status, Is.EqualTo("failed"));
            Assert.That(run?.ErrorCode, Is.EqualTo("provider_timeout"));
            Assert.That(run?.ErrorMessage, Does.Contain("stopped sending"));
        });
    }

    [Test]
    public async Task AToolNameTheProviderInventedBecomesOneMetricLabelAsync() {
        ScriptedClient client = new([
            new ToolCallDelta(0, "call-1", "make_up_a_tool", "{}"),
            new Finished("tool_calls"),
        ]);
        using MetricCapture capture = MetricCapture.Start(AssistantMetrics.MeterName);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capture.Count("assistant.tool.duration", "tool", "make_up_a_tool"), Is.Zero);
            Assert.That(capture.Count("assistant.tool.duration", "tool", AssistantMetrics.UnknownTool), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ProviderFailurePersistsPartialAssistantTextAndSafeFailureAsync() {
        ScriptedClient client = new([
            new ContentDelta("Partial answer"),
        ], new ProviderException("provider_unreachable", "The configured provider could not be reached."));
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        Run run = await database.Set<Run>().SingleAsync();
        Message assistant = await database.Set<Message>().SingleAsync(message => message.Role == "assistant");
        RunEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("failed"));
            Assert.That(run.ErrorCode, Is.EqualTo("provider_unreachable"));
            Assert.That(assistant.Content, Is.EqualTo("Partial answer"));
            Assert.That(assistant.Status, Is.EqualTo("failed"));
            Assert.That(events[^1].Type, Is.EqualTo("run.failed"));
        });
    }

    private static AssistantOptions RunTimeout(int seconds) => new() {
        RunTimeoutSeconds = seconds,
        RequestTimeoutSeconds = 3,
        ToolTimeoutSeconds = 2,
        MaxContextChars = 20_000,
    };

    private async Task<(Conversation Conversation, RunStartResult Started, RunStream Stream, RunProcessor Processor)> StartRunAsync(
        IChatCompletionClient client, AssistantOptions? options = null) {
        options ??= _options;
        ServiceProvider services = _services!;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Conversation conversation = await store.CreateAsync("owner");
        RunStartResult started = await store.StartRunAsync(conversation.Id, "owner", "Say hello", "test-model", PageAuthorPrompt.Version);
        RunRegistry registry = new(options, TimeProvider.System);
        Assert.That(registry.TryStart(started.RunId!, conversation.Id, out RunStream? stream), Is.True);
        PagePathPolicy paths = new(new ReservedPaths(ReservedPaths.Default), options);
        PageToolExecutor tools = new(services.GetRequiredService<IServiceScopeFactory>(), paths, options);
        RunProcessor processor = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            tools,
            registry,
            new ToolApprovalCoordinator(),
            new AssistantMetrics(),
            options,
            NullLogger<RunProcessor>.Instance);
        return (conversation, started, stream!, processor);
    }

    private static async Task<RunEvent[]> CollectAsync(RunStream stream) {
        List<RunEvent> events = [];
        await foreach (RunEvent @event in stream.ReadAfterAsync(0, CancellationToken.None)) {
            events.Add(@event);
        }
        return [.. events];
    }

    private sealed class ScriptedClient(
        IReadOnlyList<ChatStreamEvent> events,
        Exception? error = null,
        int delayMilliseconds = 0) : IChatCompletionClient {

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            foreach (ChatStreamEvent @event in events) {
                if (delayMilliseconds > 0) {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
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
