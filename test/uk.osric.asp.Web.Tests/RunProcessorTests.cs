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
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Every test has its own database copy, its own harness and its own services, so the fixture's tests
// are independent. They wait on a fake provider and a real database far more than they compute, so
// running them one at a time held a core idle while the wall clock ran out.
[Parallelizable(ParallelScope.All)]
public sealed class RunProcessorTests {

    private static DatabaseTemplate? s_template;

    private readonly AssistantOptions _options = TestOptions.Assistant with {
        RunTimeoutSeconds = 5,
        RequestTimeoutSeconds = 3,
        ToolTimeoutSeconds = 2,
    };

    // The window the run is given. A test that is about the request budget passes a smaller one.
    private readonly ModelWindowSource _windows = ModelWindowSource.Fixed(TestOptions.Budget);

    private readonly OwnerInterruptCoordinator _interrupts = new();

    private readonly RecordingLoggerFactory _logs = new();

    // The owner every run in this fixture belongs to, with a stamp the minted token records.
    private readonly FakeUserStore _users = new();

    public RunProcessorTests() {
        _users.Users["owner"] = new LocalUser {
            UserName = "owner",
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
    }

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
        // The run mints its view token for its owner's stamp, so the account has to be readable here.
        services.AddSingleton<IUserStore>(_users);
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
        await _services!.DisposeAsync();
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
    public async Task TheRunStoresTheEstimateAndTheAllowanceItSentWithAsync() {
        SteppedClient client = new([
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new UsageEvent(new TokenUsage(1_000, 20)),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new UsageEvent(new TokenUsage(2_000, 30)),
                new Finished("stop"),
            ],
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        Run run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<Run>().AsNoTracking().SingleAsync();
        RunEvent[] events = await CollectAsync(stream);
        JsonElement budgets = JsonElement.Parse(events.Last(@event => @event.Type == "budget.updated").DataJson).GetProperty("budgets");

        Assert.Multiple(() => {
            Assert.That(run.EstimatedTokens, Is.Not.Null, "the page rebuilds the signed error from this after a reload");
            Assert.That(run.MaxTokens, Is.Not.Null, "the completion row is read against the allowance the step had");
            Assert.That(run.MaxTokens, Is.EqualTo(client.Requests[^1].MaxTokens));
            Assert.That(budgets.TryGetProperty("estimate", out JsonElement estimate), Is.True);
            Assert.That(estimate.GetInt64(), Is.EqualTo(Math.Abs(run.EstimatedTokens!.Value - run.LastPromptTokens!.Value)));
            Assert.That(budgets.GetProperty("reply").GetInt32(), Is.EqualTo(run.MaxTokens));
        });
    }

    [Test]
    public async Task AStepWhoseRequestWasUntouchedSaysOnceWhenTheProviderCachedNoneOfItAsync() {
        SteppedClient client = new([
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new UsageEvent(new TokenUsage(1_000, 20, 0)),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new UsageEvent(new TokenUsage(2_000, 30, 0)),
                new Finished("stop"),
            ],
            [
                new ContentDelta("Done again."),
                new UsageEvent(new TokenUsage(2_500, 30, 0)),
                new Finished("stop"),
            ],
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        int warnings = _logs.Entries.Count(entry =>
            entry.Message.Contains("no cached tokens", StringComparison.Ordinal));

        Assert.That(warnings, Is.EqualTo(1), "the cache warning is said once per run, not once per step");
    }

    [Test]
    public async Task AReplyCutThroughAToolCallIsNamedRatherThanReadAsBadArgumentsAsync() {
        ScriptedClient client = new([
            new ToolCallDelta(0, "call-1", "read_page", """{"path":"/mis"""),
            new Finished("length"),
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        Run run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<Run>().AsNoTracking().SingleAsync();
        RunEvent[] events = await CollectAsync(stream);

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("failed"));
            Assert.That(run.ErrorCode, Is.EqualTo("reply_truncated"));
            Assert.That(events.Any(@event => @event.Type == "run.failed"), Is.True);
        });
    }

    [Test]
    public async Task ARunWithoutAModelWindowRefusesInsteadOfGuessingAsync() {
        ScriptedClient client = new([new ContentDelta("Never sent."), new Finished("stop")]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunWithWindowAsync(client, new ModelWindowSource());

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        Run run = await scope.ServiceProvider.GetRequiredService<StorageDbContext>().Set<Run>().AsNoTracking().SingleAsync();

        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("failed"));
            Assert.That(run.ErrorCode, Is.EqualTo("model_window_unknown"));
            Assert.That(client.Requests, Is.Empty, "a run with no budget never reaches the provider");
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
    [Category("Slow")]
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
    [Category("Slow")]
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
    [Category("Slow")]
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
    [Category("Slow")]
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
        OwnerInterrupt pending = await WaitForPendingInterruptAsync(conversation);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Multiple(() => {
            Assert.That(pending.Kind, Is.EqualTo(OwnerInterruptKind.Approval));
            Assert.That(pending.ToolCallId, Is.EqualTo("call-1"));
            Assert.That(InterruptPayloads.Approval(pending.RequestJson)?.Name, Is.EqualTo("delete_page"));
        });
        await AnswerAsync(conversation, pending, "approve");
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
    public async Task ADeniedApprovalIsAToolResultAndTheRunCarriesOnAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-1", "delete_page", """{"path":"/gone"}"""),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("I left it alone."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 5), ApprovalMode.RequireApproval);

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        OwnerInterrupt pending = await WaitForPendingInterruptAsync(conversation);
        await AnswerAsync(conversation, pending, "deny");
        await run;

        IReadOnlyList<Message> messages = await MessagesAsync(conversation);
        Run finished = await RunAsync(conversation);
        Message refusal = messages.Single(message => message.Role == "tool");
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo(RunStatus.Completed), "a denied call is an answer, not a failure");
            Assert.That(refusal.ToolCallId, Is.EqualTo("call-1"));
            Assert.That(refusal.Content, Does.Contain("The owner denied this tool call."));
            Assert.That(messages[^1].Content, Is.EqualTo("I left it alone."));
        });
    }

    [Test]
    public async Task AQuestionWaitsForTheOwnerAndItsAnswerIsTheToolResultAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-ask", "ask_user", QuestionArguments()),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("Thanks."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 5));

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        OwnerInterrupt pending = await WaitForPendingInterruptAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(pending.Kind, Is.EqualTo(OwnerInterruptKind.Question));
            Assert.That(pending.ToolCallId, Is.EqualTo("call-ask"));
            Assert.That(InterruptPayloads.Question(pending.RequestJson)?.Questions[0].Options[0].Label, Is.EqualTo("Alpha"));
        });

        await AnswerQuestionAsync(conversation, pending, Answers("Alpha", ""));
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run),
            "the answer is what the run was waiting for");
        await run;

        Run finished = await RunAsync(conversation);
        Message result = (await MessagesAsync(conversation)).Single(message => message.Role == "tool");
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo(RunStatus.Completed), $"{finished.ErrorCode}: {finished.ErrorMessage}");
            Assert.That(result.ToolCallId, Is.EqualTo("call-ask"));
            Assert.That(result.Content, Does.Contain("\"selected\":[\"Alpha\"]"));
        });
    }

    [Test]
    [Category("Slow")]
    public async Task AQuestionIsNotCutOffByTheToolTimeoutAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-ask", "ask_user", QuestionArguments()),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("Thanks."),
            new Finished("stop"),
        ]);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 1));

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        OwnerInterrupt pending = await WaitForPendingInterruptAsync(conversation);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await AnswerQuestionAsync(conversation, pending, Answers("Alpha", ""));
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run));
        await run;

        Run finished = await RunAsync(conversation);
        Message result = (await MessagesAsync(conversation)).Single(message => message.Role == "tool");
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo(RunStatus.Completed), $"{finished.ErrorCode}: {finished.ErrorMessage}");
            Assert.That(result.Content, Does.Not.Contain("time limit"), "the owner's wait is not the tool overrunning");
        });
    }

    [Test]
    public async Task AStoppedQuestionLeavesARefusalForItsCallAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-ask", "ask_user", QuestionArguments()),
            new Finished("tool_calls"),
        ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 5));

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        await WaitForPendingInterruptAsync(conversation);
        Assert.That(stream.Stop(), Is.True);
        Assert.That(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(run));
        await run;

        Run finished = await RunAsync(conversation);
        Message result = (await MessagesAsync(conversation)).Single(message => message.Role == "tool");
        OwnerInterrupt? pending = await PendingAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(finished.Status, Is.EqualTo(RunStatus.Cancelled));
            Assert.That(result.Content, Does.Contain("The run was stopped."));
            Assert.That(pending, Is.Null, "a stopped run's question is failed closed");
        });
    }

    [Test]
    [Category("Slow")]
    // The capture hears every meter in the process, so another fixture's tool call would be counted.
    [NonParallelizable]
    public async Task TheOwnersWaitIsNotCountedAsToolTimeAsync() {
        SteppedClient client = new([
            new ToolCallDelta(0, "call-ask", "ask_user", QuestionArguments()),
            new Finished("tool_calls"),
        ], [
            new ContentDelta("Thanks."),
            new Finished("stop"),
        ]);
        using MetricCapture capture = MetricCapture.Start(AssistantMetrics.MeterName);
        (Conversation conversation, RunStartResult started, _, RunProcessor processor) =
            await StartRunAsync(client, RunTimeout(seconds: 20, toolSeconds: 5));

        Task run = processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);
        OwnerInterrupt pending = await WaitForPendingInterruptAsync(conversation);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await AnswerQuestionAsync(conversation, pending, Answers("Alpha", ""));
        await run;

        IReadOnlyList<MetricCapture.Measurement> durations = capture.ForTool("assistant.tool.duration", "ask_user");
        Assert.Multiple(() => {
            Assert.That(durations, Has.Count.EqualTo(1));
            Assert.That(durations[0].Value, Is.LessThan(1), "the owner's wait is not the tool's time");
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
        OwnerInterrupt? pending = await store.GetPendingInterruptAsync(conversation.Id, "owner");

        Assert.Multiple(() => {
            Assert.That(finished?.Status, Is.EqualTo("completed"), $"{finished?.ErrorCode}: {finished?.ErrorMessage}");
            Assert.That(pending, Is.Null, "an approval the policy has refused is one the owner could only deny");
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
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            RunTimeoutSeconds = 5,
            RequestTimeoutSeconds = 3,
            ToolTimeoutSeconds = 2,
        };
        ScriptedClient client = new([new ContentDelta("Never sent."), new Finished("stop")]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client, options, budget: ContextBudget.ForPromptShare(1_000));

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
            measureOptions: (_, _, _) => Task.FromResult(TestOptions.Assistant with {
                RunTimeoutSeconds = 5,
                RequestTimeoutSeconds = 3,
                ToolTimeoutSeconds = 2,
            }),
            budget: ContextBudget.ForPromptShare((int)TokenEstimator.For(null).Estimate(MaskedBudget(seeds))));

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
        messages[oldest] = messages[oldest].Mask().Message;
        ChatRequest request = new(
            "test-model",
            messages,
            PageToolExecutor.Definitions,
            0.2,
            32_768,
            "reasoning_content");
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
        int callsPerStep = 8) => TestOptions.Assistant with {
            RunTimeoutSeconds = seconds,
            RequestTimeoutSeconds = 3,
            ToolTimeoutSeconds = toolSeconds,
            MaxSteps = steps,
            MaxToolCalls = calls,
            MaxToolCallsPerStep = callsPerStep,
        };

    private async Task<OwnerInterrupt> WaitForPendingInterruptAsync(Conversation conversation) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        for (int attempt = 0; attempt < 100; attempt++) {
            if (await store.GetPendingInterruptAsync(conversation.Id, "owner") is { } pending) {
                return pending;
            }
            await Task.Delay(50);
        }
        Assert.Fail("The run never asked its owner.");
        return null!;
    }

    private static string QuestionArguments() => """
        {"questions":[{"question":"Which?","options":[{"label":"Alpha"},{"label":"Beta"}]}]}
        """;

    private static string Answers(string selected, string text) {
        IReadOnlyList<string> chosen = selected.Length == 0 ? [] : [selected];
        return OwnerQuestions.WriteAnswers(new OwnerAnswerSet([new OwnerAnswer("Which?", null, chosen, text)]));
    }

    private async Task AnswerQuestionAsync(Conversation conversation, OwnerInterrupt pending, string resolutionJson) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Assert.That(await store.ResolveInterruptAsync(
            conversation.Id, "owner", pending.RunId, pending.Kind, pending.ToolCallId, resolutionJson),
            Is.EqualTo(InterruptResolutionResult.Resolved));
        Assert.That(_interrupts.Signal(pending.RunId), Is.True, "the processor was not waiting for the owner");
    }

    private async Task<OwnerInterrupt?> PendingAsync(Conversation conversation) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        return await store.GetPendingInterruptAsync(conversation.Id, "owner");
    }

    private async Task AnswerAsync(Conversation conversation, OwnerInterrupt pending, string decision) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Assert.That(await store.ResolveInterruptAsync(
            conversation.Id, "owner", pending.RunId, pending.Kind, pending.ToolCallId,
            InterruptPayloads.Write(new OwnerResolution(decision))), Is.EqualTo(InterruptResolutionResult.Resolved));
        Assert.That(_interrupts.Signal(pending.RunId), Is.True, "the processor was not waiting for the owner");
    }

    private Task<(Conversation Conversation, RunStartResult Started, RunStream Stream, RunProcessor Processor)> StartRunAsync(
        IChatCompletionClient client,
        AssistantOptions? options = null,
        ApprovalMode? approvalMode = null,
        Func<IConversationStore, Conversation, string, Task>? seed = null,
        Func<IConversationStore, Conversation, string, Task<AssistantOptions>>? measureOptions = null,
        ContextBudget? budget = null) => StartRunCoreAsync(
            client, options, approvalMode, seed, measureOptions, budget is null ? _windows : ModelWindowSource.Fixed(budget));

    private Task<(Conversation Conversation, RunStartResult Started, RunStream Stream, RunProcessor Processor)> StartRunWithWindowAsync(
        IChatCompletionClient client,
        ModelWindowSource windows) => StartRunCoreAsync(client, null, null, null, null, windows);

    private async Task<(Conversation Conversation, RunStartResult Started, RunStream Stream, RunProcessor Processor)> StartRunCoreAsync(
        IChatCompletionClient client,
        AssistantOptions? options,
        ApprovalMode? approvalMode,
        Func<IConversationStore, Conversation, string, Task>? seed,
        Func<IConversationStore, Conversation, string, Task<AssistantOptions>>? measureOptions,
        ModelWindowSource windows) {
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
        PageToolExecutor tools = new(
            services.GetRequiredService<IServiceScopeFactory>(), paths, options, windows,
            new AssistantMetrics(), new FakeAssistantFetcher());
        RunProcessor processor = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            tools,
            registry,
            _interrupts,
            new AssistantMetrics(),
            options,
            new ModelWindowSampler(
                services.GetRequiredService<IServiceScopeFactory>(),
                windows,
                options,
                _logs.CreateLogger<ModelWindowSampler>()),
            services.GetRequiredService<IUserStore>(),
            new ViewTokenStore(options),
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
            Assert.That(paused.Status, Is.EqualTo(RunStatus.AwaitingOwner));
            Assert.That(paused.ErrorCode, Is.Null, "running out of steps is not a failure");
            Assert.That(before.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool"]),
                "the pause writes nothing to the transcript");
        });

        await ContinueAsync(conversation, started.RunId!);
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
            Assert.That(events.Select(item => item.Type), Does.Contain("interrupt.required"));
            Assert.That(events.Select(item => item.Type), Does.Contain("interrupt.resolved"));
            Assert.That(events.Select(item => item.Type), Does.Not.Contain("run.failed"));
        });
    }

    [Test]
    public async Task TheToolCallsFigureTicksAsEachCallStartsAsync() {
        SteppedClient client = new(
            [
                new ToolCallDelta(0, "call-1", "read_page", """{"path":"/missing"}"""),
                new ToolCallDelta(1, "call-2", "read_page", """{"path":"/missing-too"}"""),
                new UsageEvent(new TokenUsage(1_000, 20)),
                new Finished("tool_calls"),
            ],
            [
                new ContentDelta("Done."),
                new UsageEvent(new TokenUsage(2_000, 30)),
                new Finished("stop"),
            ]);
        (Conversation conversation, RunStartResult started, RunStream stream, RunProcessor processor) =
            await StartRunAsync(client);

        await processor.RunAsync(new QueuedRun(conversation.Id, started.RunId!, "owner"), CancellationToken.None);

        RunEvent[] events = await CollectAsync(stream);
        int[] updated = [.. events.Select((item, index) => (Event: item, Index: index))
            .Where(item => item.Event.Type == "budget.updated").Select(item => item.Index)];
        int[] finished = [.. events.Select((item, index) => (Event: item, Index: index))
            .Where(item => item.Event.Type == "tool.finished").Select(item => item.Index)];
        long[] figures = [.. updated.Select(index =>
            JsonElement.Parse(events[index].DataJson).GetProperty("budgets").GetProperty("tool-calls").GetInt64())];
        Run stored = await RunAsync(conversation);
        int firstTick = Array.IndexOf(figures, 1L);
        int secondTick = Array.IndexOf(figures, 2L);

        Assert.Multiple(() => {
            Assert.That(figures.Take(2), Is.EqualTo([0L, 0L]), "the row must not jump to the whole step's request");
            Assert.That(firstTick, Is.GreaterThanOrEqualTo(0), "the first call did not tick the row");
            Assert.That(secondTick, Is.GreaterThan(firstTick), "the second call did not tick the row");
            Assert.That(figures[^1], Is.EqualTo(2), "the finished run counts both attempts");
            Assert.That(stored.ToolCallCount, Is.EqualTo(2), "the stored count is the attempts the row showed");
        });
        Assert.Multiple(() => {
            Assert.That(updated[firstTick], Is.LessThan(finished[0]), "the first tick is published before its call finishes");
            Assert.That(updated[secondTick], Is.LessThan(finished[1]), "the second tick is published before its call finishes");
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

        string? pausedOn = await PausedOnAsync(started.RunId!);
        IReadOnlyList<Message> before = await MessagesAsync(conversation);
        Assert.Multiple(() => {
            Assert.That(pausedOn, Is.EqualTo(RunBudget.ToolCalls));
            Assert.That(before.Select(message => message.Role), Is.EqualTo(["user", "assistant", "tool"]),
                "the call the budget forbids has not run");
        });

        await ContinueAsync(conversation, started.RunId!);
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

        Assert.That(await PausedOnAsync(started.RunId!), Is.EqualTo(RunBudget.ToolCallsPerStep));

        await ContinueAsync(conversation, started.RunId!);
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
            if (run?.Status == RunStatus.AwaitingOwner) {
                return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("The run never paused for the owner.");
    }

    private async Task<string?> PausedOnAsync(string runId) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        OwnerInterrupt? pending = await store.GetInterruptAsync(runId);
        return pending is null ? null : InterruptPayloads.Continuation(pending.RequestJson)?.Budget;
    }

    private async Task ContinueAsync(Conversation conversation, string runId) {
        await using AsyncServiceScope scope = _services!.CreateAsyncScope();
        IConversationStore store = scope.ServiceProvider.GetRequiredService<IConversationStore>();
        Assert.That(await store.ContinueRunAsync(conversation.Id, "owner", runId),
            Is.True, "the run was not waiting to continue");
        Assert.That(_interrupts.Signal(runId), Is.True, "the processor was not waiting for the owner");
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
