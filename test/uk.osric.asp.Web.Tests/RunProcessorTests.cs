// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
        MaxContextTokens = 90_000,
    };

    private readonly ToolApprovalCoordinator _approvals = new();

    private readonly RunContinuationCoordinator _continuation = new();

    private readonly RecordingLoggerFactory _logs = new();

    private string _databasePath = string.Empty;
    private ServiceProvider? _services;
    private FakePageStore _storage = null!;
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

        _storage = new FakePageStore();
        _pages = new PageHarness(_storage);
        _services = BuildServices();
    }

    private ServiceProvider BuildServices(IMemoryStore? memories = null) {
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(DatabaseTemplate.ConnectionString(_databasePath)));
        services.AddSingleton(TimeProvider.System);
        // The processor's own lines are what a failed run is diagnosed from, so the fixture keeps
        // them: AddLogging resolves ILogger<T>, and the recorder is what the loggers write through.
        services.AddLogging();
        services.AddSingleton<ILoggerFactory>(_logs);
        services.AddScoped<IConversationStore, SqliteConversationStore>();
        // The processor recalls memory once per run, so the fixture gives it the real store over the
        // same database rather than letting every run warn that memory is unavailable.
        services.AddSingleton(_options);
        if (memories is null) {
            services.AddScoped<IMemoryStore, SqliteMemoryStore>();
        } else {
            services.AddSingleton(memories);
        }
        services.AddScoped(_ => _pages!.Editor);
        return services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() {
        _services?.Dispose();
        _pages?.Dispose();
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task AMemoryStoreThatThrowsCostsTheRunItsMemoryAndNothingElseAsync() {
        _services!.Dispose();
        _services = BuildServices(new FailingMemoryStore());
        ScriptedClient client = new([new ContentDelta("Hello."), new Finished("stop")]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Run run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<Run>().SingleAsync();
        RunEvent[] events = await CollectAsync(stream);
        RunEvent warning = events.Single(item => item.Type == "run.warning");

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("completed"), "memory is an enhancement and never takes a run down");
            Assert.That(warning.DataJson, Does.Contain("memory_unavailable"));
            Assert.That(events[^1].Type, Is.EqualTo("run.completed"));
        });
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
    public async Task ASteppedRunSumsItsUsageAndKeepsTheLastStepsOutputAsync() {
        SteppedClient client = new([
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new UsageEvent(new TokenUsage(1_000, 20, 600, 5)),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new UsageEvent(new TokenUsage(2_000, 30, 1_200, 7)),
                new Finished("stop"),
            ],
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        RunEvent[] events = await CollectAsync(stream);
        JsonDocument usage = JsonDocument.Parse(events.Last(@event => @event.Type == "budget.updated").DataJson);
        JsonElement totals = usage.RootElement.GetProperty("usage");

        Assert.Multiple(() => {
            Assert.That(run.PromptTokens, Is.EqualTo(3_000));
            Assert.That(run.CompletionTokens, Is.EqualTo(50));
            Assert.That(run.CachedTokens, Is.EqualTo(1_800));
            Assert.That(run.ReasoningTokens, Is.EqualTo(12));
            Assert.That(run.OutputTokens, Is.EqualTo(30));
            Assert.That(run.RequestBytes, Is.GreaterThan(0));
            Assert.That(run.LastPromptTokens, Is.EqualTo(2_000));
            Assert.That(totals.GetProperty("prompt").GetInt32(), Is.EqualTo(3_000));
            Assert.That(totals.GetProperty("cached").GetInt32(), Is.EqualTo(1_800));
            Assert.That(totals.GetProperty("completion").GetInt32(), Is.EqualTo(50));
            Assert.That(totals.GetProperty("reasoning").GetInt32(), Is.EqualTo(12));
        });
        usage.Dispose();
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
    public async Task AToolThatOutlastsTheRunTimeoutDoesNotFailTheRunAsync() {
        _storage.ReadDelay = TimeSpan.FromSeconds(2);
        SteppedClient client = new([
            new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("There is no such page."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 1, toolSeconds: 5));

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? run = await store.GetLastRunAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(run?.Status, Is.EqualTo("completed"));
            Assert.That(run?.ErrorCode, Is.Null);
        });
    }

    [Test]
    public async Task ARunWaitingForApprovalOutlivesTheRunTimeoutAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-1", "delete_page", """{"path":"/gone"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("Deleted."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 1, toolSeconds: 5), ApprovalMode.RequireApproval);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPendingApprovalAsync(conversation);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.That(_approvals.Signal(started.RunId!, "call-1", ApprovalResolutionResult.Approved), Is.True);
        await run;

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? finished = await store.GetLastRunAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(finished?.Status, Is.EqualTo("completed"), $"{finished?.ErrorCode}: {finished?.ErrorMessage}");
            Assert.That(finished?.ErrorCode, Is.Null);
        });
    }

    [Test]
    public async Task AWriteToAShellFileIsRefusedWithoutAnApprovalAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-1", "write_page", """{"path":"/login","content":"x"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("That key is the sign-in form, so I left it alone."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 5), ApprovalMode.RequireApproval);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run),
            "the refusal must not park the run on an approval nobody will answer");
        await run;

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? finished = await store.GetLastRunAsync(conversation.Id, "owner");
        IReadOnlyList<ToolApproval> pending = await store.GetPendingApprovalsAsync(conversation.Id, "owner");

        Assert.Multiple(() => {
            Assert.That(finished?.Status, Is.EqualTo("completed"), $"{finished?.ErrorCode}: {finished?.ErrorMessage}");
            Assert.That(pending, Is.Empty, "an approval the policy has refused is one the owner could only deny");
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
        // The run has to be given a turn that ends: a script that only ever asks for the tool would
        // step until the tool-call budget paused it for a continuation this test never sends, and the
        // await below would never return.
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "make_up_a_tool", "{}"),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("I cannot use that tool."),
                new Finished("stop"),
            ]);
        using MetricCapture capture = MetricCapture.Start(AssistantMetrics.MeterName);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run),
            "a run that parks itself fails this test rather than hanging the fixture");
        await run;

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

    [Test]
    public async Task AProviderFailureIsLoggedWithItsExceptionAndTheModelAsync() {
        ProviderException failure = new("provider_unreachable", "The configured provider could not be reached.",
            new HttpRequestException("No such host is known."));
        ScriptedClient client = new([], failure);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        RecordingLoggerFactory.Entry entry = _logs.Entries.Single(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("provider request", StringComparison.Ordinal));

        Assert.Multiple(() => {
            Assert.That(entry.Exception, Is.SameAs(failure), "the line is worth nothing without the socket or TLS failure inside it");
            Assert.That(entry.Scope["Model"], Is.EqualTo("test-model"), "the model is the fact only the processor learns");
        });
    }

    [Test]
    public async Task TheWorkersScopeNamesTheRunOnEveryLineTheProcessorWritesAsync() {
        ScriptedClient client = new([new ContentDelta("Hello."), new Finished("stop")]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        // The worker owns the run's scope, so it is the worker that has to be driven for the ids to
        // appear; the run itself is the processor the fixture already built.
        RunQueue queue = new(_options);
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILoggerFactory>(_logs)
            .AddScoped(_ => processor)
            .BuildServiceProvider();

        RunWorker worker = new(queue, services.GetRequiredService<IServiceScopeFactory>(), _options, _logs.CreateLogger<RunWorker>());
        await worker.StartAsync(CancellationToken.None);
        try {
            Assert.That(queue.TryEnqueue(new QueuedRun(conversation.Id, started.RunId!, "owner")), Is.True);

            RecordingLoggerFactory.Entry finished = await WaitForEntryAsync(
                entry => entry.Message.Contains("finished as", StringComparison.Ordinal));

            Assert.Multiple(() => {
                Assert.That(finished.Scope["RunId"], Is.EqualTo(started.RunId));
                Assert.That(finished.Scope["ConversationId"], Is.EqualTo(conversation.Id));
                Assert.That(finished.Scope["UserName"], Is.EqualTo("owner"));
                Assert.That(finished.Scope["Model"], Is.EqualTo("test-model"),
                    "the processor's own scope nests inside the worker's rather than repeating its ids");
            });
        } finally {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task TheStepBudgetIsTheRequestThatStepSent() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("There is no such page."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        RunEvent[] events = await CollectAsync(stream);
        long[] figures = [.. events
            .Where(@event => @event.Type == "budget.updated")
            .Select(@event => ContextFigure(@event.DataJson))];

        Assert.Multiple(() => {
            Assert.That(client.Requests, Has.Count.EqualTo(2));
            foreach (ChatRequest request in client.Requests) {
                Assert.That(figures, Does.Contain(TokenEstimator.For(null).Estimate(ChatRequestJson.MeasureBytes(request))),
                    "the figure a step published is the request it sent");
            }
        });
    }

    [Test]
    public async Task AnOverBudgetRunFailsBeforeTheProviderIsCalled() {
        AssistantOptions options = new() {
            RunTimeoutSeconds = 5,
            RequestTimeoutSeconds = 3,
            ToolTimeoutSeconds = 2,
            MaxContextTokens = 1_000,
        };
        ScriptedClient client = new([new ContentDelta("Never sent."), new Finished("stop")]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client, options);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Run? run = await store.GetLastRunAsync(conversation.Id, "owner");
        RunEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run?.Status, Is.EqualTo("failed"));
            Assert.That(run?.ErrorCode, Is.EqualTo("context_too_long"));
            Assert.That(client.Requests, Is.Empty, "a request that cannot fit is never sent");
            Assert.That(events, Has.Some.Matches<RunEvent>(@event => @event is { Type: "run.failed" }
                && @event.DataJson.Contains("context_too_long", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task AnOverBudgetRunRaisesTheMaskedWarningOnce() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-new", "read_page", """{"path":"/missing"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("Done."),
            new Finished("stop"),
        ]);
        List<(string Id, string Content)> seeds = [.. Enumerable.Range(0, 9)
            .Select(index => ($"call-{index}", new string('x', 1_000)))];

        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) = await StartRunAsync(
            client,
            seed: (store, created, runId) => SeedAsync(store, created.Id, runId, seeds),
            measureOptions: (_, _, _) => Task.FromResult(new AssistantOptions {
                RunTimeoutSeconds = 5,
                RequestTimeoutSeconds = 3,
                ToolTimeoutSeconds = 2,
                MaxContextTokens = (int)TokenEstimator.For(null).Estimate(MaskedBudget(seeds)),
            }));

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        RunEvent[] events = await CollectAsync(stream);
        int warnings = events.Count(@event => @event.Type == "run.warning"
            && WarningCode(@event.DataJson) == "context_masked");

        Assert.Multiple(() => {
            Assert.That(client.Requests, Has.Count.EqualTo(2), "both steps had to make the request fit");
            Assert.That(client.Requests[1].Messages.Count(message => message.Content.StartsWith("[tool result elided:", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(warnings, Is.EqualTo(1), "a hundred steps that masked are one fact, not a hundred");
        });
    }

    private static long ContextFigure(string dataJson) {
        using JsonDocument document = JsonDocument.Parse(dataJson);
        return document.RootElement.GetProperty("budgets").GetProperty("context").GetInt64();
    }

    private static string? WarningCode(string dataJson) {
        using JsonDocument document = JsonDocument.Parse(dataJson);
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task SeedAsync(
        IConversationStore store,
        long conversationId,
        string runId,
        IReadOnlyList<(string Id, string Content)> seeds) {
        foreach ((string id, string content) in seeds) {
            await store.AppendMessageAsync(conversationId, runId,
                new MessageInput("assistant", "", ToolCallsJson: Call(id)), CancellationToken.None);
            await store.AppendMessageAsync(conversationId, runId,
                new MessageInput("tool", content, ToolCallId: id, ToolName: "read_page"), CancellationToken.None);
        }
    }

    // The budget that fails unless the oldest of nine results is masked: it is the one that has fallen
    // out of the eight-result window.
    private static long MaskedBudget(IReadOnlyList<(string Id, string Content)> seeds) {
        List<ChatMessage> messages = [new("system", PageAuthorPrompt.Build()), new("user", "Say hello")];
        foreach ((string id, string content) in seeds) {
            messages.Add(new("assistant", "", Call(id)));
            messages.Add(new("tool", content, ToolCallId: id, ToolName: "read_page"));
        }
        int oldest = messages.FindIndex(message => message.Role == "tool");
        messages[oldest] = ObservationMasker.MaskMessage(messages[oldest]).Message;
        ChatRequest request = new(
            "test-model",
            messages,
            PageToolExecutor.Definitions,
            0.2,
            32_768,
            "reasoning_content",
            IncludeUsage: true);
        return ChatRequestJson.MeasureBytes(request);
    }

    private static string Call(string id) => JsonSerializer.Serialize(new[] {
        new { id, type = "function", function = new { name = "read_page", arguments = """{"path":"/missing"}""" } },
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<RecordingLoggerFactory.Entry> WaitForEntryAsync(Func<RecordingLoggerFactory.Entry, bool> matches) {
        for (int attempt = 0; attempt < 250; attempt++) {
            RecordingLoggerFactory.Entry? found = _logs.Entries.LastOrDefault(matches);
            if (found is not null) {
                return found;
            }

            await Task.Delay(20);
        }

        Assert.Fail("The run never wrote the line the test waited for.");
        throw new InvalidOperationException("unreachable");
    }

    private static AssistantOptions RunTimeout(
        int seconds,
        int toolSeconds = 2,
        int steps = 40,
        int calls = 16,
        int callsPerStep = 8) => new() {
            RunTimeoutSeconds = seconds,
            RequestTimeoutSeconds = 3,
            ToolTimeoutSeconds = toolSeconds,
            MaxContextTokens = 90_000,
            MaxSteps = steps,
            MaxToolCalls = calls,
            MaxToolCallsPerStep = callsPerStep,
        };

    private async Task WaitForPendingApprovalAsync(Conversation conversation) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        for (int attempt = 0; attempt < 100; attempt++) {
            if ((await store.GetPendingApprovalsAsync(conversation.Id, "owner")).Count > 0) {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail("The run never asked for approval.");
    }

    private async Task<(Conversation Conversation, RunStartResult Started, RunStream Stream, RunProcessor Processor)> StartRunAsync(
        IChatCompletionClient client,
        AssistantOptions? options = null,
        ApprovalMode? approvalMode = null,
        Func<IConversationStore, Conversation, string, Task>? seed = null,
        Func<IConversationStore, Conversation, string, Task<AssistantOptions>>? measureOptions = null) {
        ServiceProvider services = _services!;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Conversation conversation = await store.CreateAsync("owner");
        if (approvalMode is not null) {
            Assert.That(await store.SetApprovalModeAsync(conversation.Id, "owner", approvalMode.Value), Is.True);
        }
        RunStartResult started = await store.StartRunAsync(conversation.Id, "owner", "Say hello", "test-model", PageAuthorPrompt.Version);
        if (seed is not null) {
            await seed(store, conversation, started.RunId!);
        }
        options ??= measureOptions is null ? _options : await measureOptions(store, conversation, started.RunId!);
        RunRegistry registry = new(options, TimeProvider.System);
        Assert.That(registry.TryStart(started.RunId!, conversation.Id, out RunStream? stream), Is.True);
        PagePathPolicy paths = new(new ReservedPaths(ReservedPaths.Default), options);
        PageToolExecutor tools = new(services.GetRequiredService<IServiceScopeFactory>(), paths, options, new AssistantMetrics());
        RunProcessor processor = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            tools,
            registry,
            _approvals,
            _continuation,
            new AssistantMetrics(),
            options,
            _logs.CreateLogger<RunProcessor>());
        return (conversation, started, stream!, processor);
    }

    [Test]
    public async Task ARunThatUsesItsStepsPausesForTheOwnerInsteadOfFailingAsync() {
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new Finished("stop"),
            ]);
        AssistantOptions options = RunTimeout(seconds: 20, steps: 1);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client, options);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPauseAsync(started.RunId!);

        Run paused = await RunAsync(conversation);
        IReadOnlyList<Message> before = await MessagesAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(paused.Status, Is.EqualTo(RunBudget.Awaiting));
            Assert.That(paused.PausedBudget, Is.EqualTo(RunBudget.Steps));
            Assert.That(paused.ErrorCode, Is.Null, "running out of steps is not a failure");
            Assert.That(before.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool"]),
                "the pause writes nothing to the transcript");
        });

        await ContinueAsync(conversation, started.RunId!, options);
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run),
            "continuing resumes the run");
        await run;

        Run finished = await RunAsync(conversation);
        IReadOnlyList<Message> after = await MessagesAsync(conversation);
        RunEvent[] events = await CollectAsync(stream);
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo("completed"));
            Assert.That(finished.StepLimit, Is.EqualTo(2), "one more window of the steps that ran out");
            Assert.That(finished.ToolCallLimit, Is.Null, "the tool-call budget was not the one that ran out");
            Assert.That(after.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool", "assistant"]));
            Assert.That(after[^1].Content, Is.EqualTo("Done."), "the model answered the step it was about to make");
            Assert.That(events.Select(item => item.Type), Does.Contain("run.paused"));
            Assert.That(events.Select(item => item.Type), Does.Contain("run.resumed"));
            Assert.That(events.Select(item => item.Type), Does.Not.Contain("run.failed"));
        });
    }

    [Test]
    public async Task ARunThatUsesItsToolCallsPausesBeforeTheCallItCannotMakeAsync() {
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new ToolCallDelta(1, "call-2", "read_page", """{"path":"/missing-too"}"""),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new Finished("stop"),
            ]);
        AssistantOptions options = RunTimeout(seconds: 20, calls: 1);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, options);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPauseAsync(started.RunId!);

        string? pausedOn = await PausedBudgetAsync(started.RunId!);
        IReadOnlyList<Message> before = await MessagesAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(pausedOn, Is.EqualTo(RunBudget.ToolCalls));
            Assert.That(before.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool"]),
                "the call the budget forbids has not run");
        });

        await ContinueAsync(conversation, started.RunId!, options);
        await run;

        Run finished = await RunAsync(conversation);
        IReadOnlyList<Message> after = await MessagesAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo("completed"));
            Assert.That(finished.ToolCallLimit, Is.EqualTo(2));
            Assert.That(finished.StepLimit, Is.Null);
            Assert.That(after.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool", "tool", "assistant"]));
        });
    }

    [Test]
    public async Task OneStepThatAsksForTooManyCallsPausesTooAsync() {
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new ToolCallDelta(1, "call-2", "read_page", """{"path":"/missing-too"}"""),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new Finished("stop"),
            ]);
        AssistantOptions options = RunTimeout(seconds: 20, calls: 16, callsPerStep: 1);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, options);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPauseAsync(started.RunId!);

        Assert.That(await PausedBudgetAsync(started.RunId!), Is.EqualTo(RunBudget.ToolCallsPerStep));

        await ContinueAsync(conversation, started.RunId!, options);
        await run;

        Run finished = await RunAsync(conversation);
        IReadOnlyList<Message> after = await MessagesAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo("completed"));
            Assert.That(after.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool", "tool", "assistant"]));
        });
    }

    [Test]
    public async Task StoppingAPausedRunEndsItCancelledRatherThanWaitingAsync() {
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new Finished("stop"),
            ]);
        AssistantOptions options = RunTimeout(seconds: 20, steps: 1);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client, options);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPauseAsync(started.RunId!);

        _ = stream.Stop();

        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run),
            "a stop must not wait for an answer nobody is going to give");
        await run;
        Assert.That((await RunAsync(conversation)).Status, Is.EqualTo("cancelled"));
    }

    private async Task WaitForPauseAsync(string runId) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        for (int attempt = 0; attempt < 200; attempt++) {
            Run? run = await database.Set<Run>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == runId);
            if (run?.Status == RunBudget.Awaiting) {
                return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("The run never paused for the owner.");
    }

    private async Task<string?> PausedBudgetAsync(string runId) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        return (await database.Set<Run>().AsNoTracking().SingleAsync(item => item.Id == runId)).PausedBudget;
    }

    private async Task ContinueAsync(Conversation conversation, string runId, AssistantOptions options) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Assert.That(await store.ContinueRunAsync(conversation.Id, "owner", runId, options.MaxSteps, options.MaxToolCalls),
            Is.True, "the run was not waiting to continue");
        Assert.That(_continuation.Signal(runId), Is.True, "the processor was not waiting for the decision");
    }

    private async Task<IReadOnlyList<Message>> MessagesAsync(Conversation conversation) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        return await store.GetMessagesAsync(conversation.Id, "owner");
    }

    private async Task<Run> RunAsync(Conversation conversation) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        return (await store.GetLastRunAsync(conversation.Id, "owner"))!;
    }

    private static async Task<RunEvent[]> CollectAsync(RunStream stream) {
        List<RunEvent> events = [];
        await foreach (RunEvent @event in stream.ReadAfterAsync(0, CancellationToken.None)) {
            events.Add(@event);
        }
        return [.. events];
    }

    private sealed class FailingMemoryStore : IMemoryStore {
        public Task<IReadOnlyList<MemoryCandidate>> RecallAsync(string owner, string queryTerms, int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<IReadOnlyList<MemoryCandidate>> StickyAsync(string owner, long conversationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<IReadOnlyList<MemoryCandidate>> PinnedAsync(string owner, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<IReadOnlyList<MemoryCandidate>> ListAsync(string owner, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<MemoryCandidate?> GetAsync(string owner, long id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<MemoryWriteResult> EditAsync(string owner, long id, int expectedVersion, MemoryKind kind, string content, bool pinned, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<MemoryWriteResult> RememberAsync(string owner, MemoryKind kind, string content, MemorySource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<MemoryWriteResult> UpdateAsync(string owner, long id, int expectedVersion, string content, MemorySource source, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<bool> ForgetAsync(string owner, long id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task TouchAsync(string owner, IReadOnlyList<long> ids, long conversationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<int> CountAsync(string owner, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<int> ClearProvenanceAsync(long conversationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");

        public Task<int> DeleteForOwnerAsync(string owner, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The memory database is unavailable.");
    }

    private sealed class ScriptedClient(
        IReadOnlyList<ChatStreamEvent> events,
        Exception? error = null,
        int delayMilliseconds = 0) : IChatCompletionClient {

        private readonly List<ChatRequest> _requests = [];

        public IReadOnlyList<ChatRequest> Requests => _requests;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            _requests.Add(request);
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

    /// <summary>A client that answers each provider request with the next script in the list.</summary>
    private sealed class SteppedClient(params IReadOnlyList<ChatStreamEvent>[] scripts) : IChatCompletionClient {
        private readonly List<ChatRequest> _requests = [];
        private int _next;

        public IReadOnlyList<ChatRequest> Requests => _requests;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            _requests.Add(request);
            IReadOnlyList<ChatStreamEvent> script = scripts[Math.Min(_next, scripts.Length - 1)];
            _next++;
            foreach (ChatStreamEvent @event in script) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
