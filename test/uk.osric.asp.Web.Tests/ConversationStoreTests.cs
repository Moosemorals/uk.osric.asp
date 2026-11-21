// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Each test owns its storage - a database file of its own, or a store and harness of its own - so the
// tests here are independent, and waiting for one to finish before starting the next only makes the
// suite slower than the work it is doing.
[Parallelizable(ParallelScope.All)]
public sealed class ConversationStoreTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("assistant-conversations");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("assistant-conversations");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task NewConversationUsesGeneratedTitleAndAlwaysApproveAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);

        Conversation created = await conversations.CreateAsync("owner");

        Assert.Multiple(() => {
            Assert.That(created.Title, Is.EqualTo("New conversation"));
            Assert.That(created.HasGeneratedTitle, Is.True);
            Assert.That(created.ApprovalMode, Is.EqualTo("AlwaysApprove"));
            Assert.That(created.NextMessageSequence, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConversationReadsAndListsAreScopedToTheOwnerAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation first = await conversations.CreateAsync("owner");
        Conversation other = await conversations.CreateAsync("other");

        Assert.That(await conversations.GetAsync(other.Id, "owner"), Is.Null);
        Assert.That((await conversations.ListAsync("owner")).Select(item => item.Id), Is.EqualTo([first.Id]));
    }

    [Test]
    public async Task MessageReadsAreScopedToTheOwnerAndConversationAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        Conversation other = await conversations.CreateAsync("owner");
        await conversations.StartRunAsync(conversation.Id, "owner", "Hello", "model", "prompt");
        Message stored = (await conversations.GetMessagesAsync(conversation.Id, "owner")).Single();
        Message? found = await conversations.GetMessageAsync(conversation.Id, "owner", stored.Id);
        Message? wrongOwner = await conversations.GetMessageAsync(conversation.Id, "other", stored.Id);
        Message? wrongConversation = await conversations.GetMessageAsync(other.Id, "owner", stored.Id);
        Message? missing = await conversations.GetMessageAsync(conversation.Id, "owner", stored.Id + 1);

        Assert.Multiple(() => {
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Id, Is.EqualTo(stored.Id));
            Assert.That(wrongOwner, Is.Null);
            Assert.That(wrongConversation, Is.Null);
            Assert.That(missing, Is.Null);
        });
    }

    [Test]
    public async Task RenameAndApprovalModeUpdateOnlyTheOwnedConversationAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");

        Assert.That(await conversations.RenameAsync(conversation.Id, "owner", "My pages"), Is.True);
        Assert.That(await conversations.SetApprovalModeAsync(conversation.Id, "owner", ApprovalMode.RequireApproval), Is.True);
        Assert.That(await conversations.RenameAsync(conversation.Id, "other", "No"), Is.False);

        Conversation saved = (await conversations.GetAsync(conversation.Id, "owner"))!;
        Assert.Multiple(() => {
            Assert.That(saved.Title, Is.EqualTo("My pages"));
            Assert.That(saved.HasGeneratedTitle, Is.False);
            Assert.That(saved.ApprovalMode, Is.EqualTo("RequireApproval"));
        });
    }

    [Test]
    public async Task DeletingConversationCascadesThroughMessagesRunsAndApprovalsAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        string runId = Guid.NewGuid().ToString("N");
        database.Add(new Run {
            Id = runId,
            ConversationId = conversation.Id,
            Status = "completed",
            Model = "test",
            ApprovalMode = "AlwaysApprove",
            PromptVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            StepCount = 1,
        });
        database.Add(new Message {
            ConversationId = conversation.Id,
            RunId = runId,
            Sequence = 1,
            Role = "user",
            Content = "message",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        database.Add(new OwnerInterrupt {
            RunId = runId,
            Kind = OwnerInterruptKind.Approval,
            Status = OwnerInterruptStatus.Resolved,
            ToolCallId = "call-1",
            RequestJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();

        Assert.That(await conversations.DeleteAsync(conversation.Id, "owner"), Is.True);
        Assert.Multiple(() => {
            Assert.That(database.Set<Conversation>().Count(), Is.Zero);
            Assert.That(database.Set<Run>().Count(), Is.Zero);
            Assert.That(database.Set<Message>().Count(), Is.Zero);
            Assert.That(database.Set<OwnerInterrupt>().Count(), Is.Zero);
        });
    }

    [Test]
    public async Task StartingRunStoresTheUserMessageAndApprovalSnapshotInOneTransactionAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        await conversations.SetApprovalModeAsync(conversation.Id, "owner", ApprovalMode.RequireApproval);

        RunStartResult started = await conversations.StartRunAsync(
            conversation.Id, "owner", "Build a page", "model", "prompt-v1");

        Assert.That(started.Status, Is.EqualTo(RunStartStatus.Started));
        Assert.That(started.UserMessageSequence, Is.EqualTo(1));
        Assert.That(started.RunId, Is.Not.Null);
        Run run = (await conversations.GetActiveRunAsync(conversation.Id, "owner"))!;
        Message userMessage = (await conversations.GetMessagesAsync(conversation.Id, "owner")).Single();
        Assert.Multiple(() => {
            Assert.That(run.ApprovalMode, Is.EqualTo("RequireApproval"));
            Assert.That(userMessage.Role, Is.EqualTo("user"));
            Assert.That(userMessage.Content, Is.EqualTo("Build a page"));
            Assert.That(userMessage.RunId, Is.EqualTo(started.RunId));
        });
    }

    [Test]
    public async Task OnlyTheFirstUserMessageGeneratesTheConversationTitleAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult first = await conversations.StartRunAsync(conversation.Id, "owner", "First request", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(first.RunId!, "completed");
        RunStartResult second = await conversations.StartRunAsync(conversation.Id, "owner", "Another request", "model", "prompt-v1");

        Assert.That(second.Status, Is.EqualTo(RunStartStatus.Started));
        Assert.That((await conversations.GetAsync(conversation.Id, "owner"))?.Title, Is.EqualTo("First request"));
    }

    [Test]
    public async Task SecondActiveRunIsRejectedWithoutAllocatingAnotherMessageSequenceAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        await conversations.StartRunAsync(conversation.Id, "owner", "first", "model", "prompt-v1");

        RunStartResult second = await conversations.StartRunAsync(
            conversation.Id, "owner", "second", "model", "prompt-v1");

        Conversation saved = (await conversations.GetAsync(conversation.Id, "owner"))!;
        Assert.That(second.Status, Is.EqualTo(RunStartStatus.AlreadyActive));
        Assert.That(saved.NextMessageSequence, Is.EqualTo(2));
        Assert.That((await conversations.GetMessagesAsync(conversation.Id, "owner")).Select(message => message.Content), Is.EqualTo(["first"]));
    }

    [Test]
    public async Task ConcurrentTranscriptAppendsReceiveUniqueSequencesAsync() {
        long conversationId;
        string runId;
        await using (StorageDbContext database = new(_options)) {
            Conversation conversation = await NewStore(database).CreateAsync("owner");
            RunStartResult start = await NewStore(database).StartRunAsync(
                conversation.Id, "owner", "first", "model", "prompt-v1");
            conversationId = conversation.Id;
            runId = start.RunId!;
        }

        const int Appends = 8;
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<long?>[] appends = [.. Enumerable.Range(0, Appends).Select(async index => {
            await using StorageDbContext database = new(_options);
            await gate.Task;
            return await NewStore(database).AppendMessageAsync(
                conversationId, runId, new MessageInput("assistant", $"reply-{index}"));
        })];
        gate.SetResult();

        long?[] ids = await Task.WhenAll(appends);
        await using StorageDbContext reader = new(_options);
        IReadOnlyList<Message> messages = await NewStore(reader).GetMessagesAsync(conversationId, "owner");

        Assert.That(ids, Is.All.Not.Null);
        Assert.That(messages.Select(message => message.Sequence), Is.EqualTo(Enumerable.Range(1, Appends + 1).Select(value => (long)value)));
    }

    [Test]
    public async Task AnAppendedMessageReportsTheIdTheContentRouteServesAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation first = await conversations.CreateAsync("owner");
        RunStartResult firstRun = await conversations.StartRunAsync(first.Id, "owner", "first", "model", "prompt-v1");
        await conversations.AppendMessageAsync(first.Id, firstRun.RunId!, new MessageInput("assistant", "reply"));

        Conversation second = await conversations.CreateAsync("owner");
        RunStartResult secondRun = await conversations.StartRunAsync(second.Id, "owner", "second", "model", "prompt-v1");
        long? reported = await conversations.AppendMessageAsync(
            second.Id, secondRun.RunId!, new MessageInput("assistant", "reply"));

        Message stored = (await conversations.GetMessagesAsync(second.Id, "owner")).Single(message => message.Role == "assistant");
        Assert.Multiple(() => {
            Assert.That(stored.Sequence, Is.Not.EqualTo(stored.Id), "the case needs a sequence that has drifted from the id");
            Assert.That(reported, Is.EqualTo(stored.Id), "the transcript fetches the message by the id this call reports");
        });
    }

    [Test]
    public async Task AnInterruptIsResolvedOnlyWhileItIsPendingForTheOwnedActiveRunAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        string resolution = InterruptPayloads.Write(new OwnerResolution("approve"));
        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval()), Is.True);

        InterruptResolutionResult? resolved = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Approval, "call-1", resolution);
        InterruptResolutionResult repeated = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Approval, "call-1", resolution);
        InterruptResolutionResult conflicting = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Approval, "call-1",
            InterruptPayloads.Write(new OwnerResolution("deny")));
        InterruptResolutionResult wrongKind = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Continuation, "call-1", resolution);

        Run run = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;
        OwnerInterrupt? pendingAfter = await conversations.GetPendingInterruptAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(resolved, Is.EqualTo(InterruptResolutionResult.Resolved));
            Assert.That(repeated, Is.EqualTo(InterruptResolutionResult.Resolved), "a double click is one answer");
            Assert.That(conflicting, Is.EqualTo(InterruptResolutionResult.Conflict));
            Assert.That(wrongKind, Is.EqualTo(InterruptResolutionResult.Conflict), "a form for another kind is stale");
            Assert.That(run.Status, Is.EqualTo(RunStatus.Running), "resolving moves the run on");
            Assert.That(pendingAfter, Is.Null);
        });
    }

    [Test]
    public async Task AStrangerCannotResolveAnInterruptAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval());

        InterruptResolutionResult result = await conversations.ResolveInterruptAsync(
            conversation.Id, "someone", start.RunId!, OwnerInterruptKind.Approval, "call-1",
            InterruptPayloads.Write(new OwnerResolution("approve")));

        OwnerInterrupt? still = await conversations.GetPendingInterruptAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(result, Is.EqualTo(InterruptResolutionResult.NotFound));
            Assert.That(still, Is.Not.Null);
        });
    }

    [Test]
    public async Task ARunHasAtMostOnePendingInterruptAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);

        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval()), Is.True);
        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Approval, "call-2", Approval()), Is.False,
            "the run is already waiting on its owner");

        OwnerInterrupt pending = (await conversations.GetPendingInterruptAsync(conversation.Id, "owner"))!;
        Run run = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;
        Assert.Multiple(() => {
            Assert.That(pending.ToolCallId, Is.EqualTo("call-1"), "the first question is the one the owner was shown");
            Assert.That(run.Status, Is.EqualTo(RunStatus.AwaitingOwner));
        });
    }

    [Test]
    public async Task AnInterruptIsOpenedOnlyWhileItsRunIsRunningAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval()), Is.False,
            "a queued run has not reached a point where it can ask");

        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Cancelled);
        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval()), Is.False);

        Assert.That(await conversations.GetPendingInterruptAsync(conversation.Id, "owner"), Is.Null);
    }

    [Test]
    public async Task StoppingARunFailsItsPendingInterruptWithoutExecutingItAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(start.RunId!, OwnerInterruptKind.Approval, "call-1", Approval());

        Assert.That(await conversations.FailPendingInterruptAsync(start.RunId!, "stop"), Is.EqualTo(1));

        OwnerInterrupt interrupt = (await database.Set<OwnerInterrupt>().AsNoTracking().SingleAsync())!;
        InterruptResolutionResult afterFailure = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Approval, "call-1",
            InterruptPayloads.Write(new OwnerResolution("approve")));
        Assert.Multiple(() => {
            Assert.That(interrupt.Status, Is.EqualTo(OwnerInterruptStatus.Failed));
            Assert.That(interrupt.FailureCode, Is.EqualTo("stop"));
            Assert.That(afterFailure, Is.EqualTo(InterruptResolutionResult.Conflict));
        });
    }

    [Test]
    public async Task StartupRecoveryInterruptsRunsAndRepairsDanglingToolCallsAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        string toolCalls = "[{\"id\":\"call-1\",\"type\":\"function\",\"function\":{\"name\":\"write_page\",\"arguments\":\"{}\"}},{\"id\":\"call-2\",\"type\":\"function\",\"function\":{\"name\":\"delete_page\",\"arguments\":\"{}\"}}]";
        await conversations.AppendMessageAsync(conversation.Id, start.RunId!, new MessageInput("assistant", "", ToolCallsJson: toolCalls));
        await conversations.AppendMessageAsync(conversation.Id, start.RunId!, new MessageInput("tool", "done", ToolCallId: "call-1", ToolName: "write_page"));
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(start.RunId!, OwnerInterruptKind.Approval, "call-2", Approval());

        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.EqualTo(1));
        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.Zero);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        OwnerInterrupt interrupt = await database.Set<OwnerInterrupt>().AsNoTracking().SingleAsync();
        Message repaired = await database.Set<Message>().AsNoTracking()
            .SingleAsync(message => message.ToolCallId == "call-2");
        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo(RunStatus.Interrupted));
            Assert.That(run.ErrorCode, Is.EqualTo("restart"));
            Assert.That(interrupt.Status, Is.EqualTo(OwnerInterruptStatus.Failed));
            Assert.That(interrupt.FailureCode, Is.EqualTo("restart"));
            Assert.That(repaired.Content, Does.Contain("host restarted"));
            Assert.That(repaired.ToolName, Is.EqualTo("delete_page"));
        });
    }

    [Test]
    public async Task RecordStepAddsTheReportedUsageToTheRunAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.RecordStepAsync(start.RunId!, 4_000, 1_100, 100_000,
            new TokenUsage(1_000, 20, 600, 5), CancellationToken.None), Is.True);
        Assert.That(await conversations.RecordStepAsync(start.RunId!, 5_000, 2_100, 90_000,
            new TokenUsage(2_000, 30, 1_200, 7), CancellationToken.None), Is.True);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(run.StepCount, Is.EqualTo(2));
            Assert.That(run.ToolCallCount, Is.Zero, "a step records no calls; they are counted as they run");
            Assert.That(run.PromptTokens, Is.EqualTo(3_000));
            Assert.That(run.CompletionTokens, Is.EqualTo(50));
            Assert.That(run.CachedTokens, Is.EqualTo(1_800));
            Assert.That(run.ReasoningTokens, Is.EqualTo(12));
            Assert.That(run.OutputTokens, Is.EqualTo(30), "the output budget counts the last step");
            Assert.That(run.RequestBytes, Is.EqualTo(5_000));
            Assert.That(run.LastPromptTokens, Is.EqualTo(2_000));
            Assert.That(run.EstimatedTokens, Is.EqualTo(2_100), "the estimate is what the error row is measured from");
            Assert.That(run.MaxTokens, Is.EqualTo(90_000), "the allowance is what the completion row is read against");
        });
    }

    [Test]
    public async Task ANullUsageStillCountsTheStepAndLeavesTheTotalsAloneAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.RecordStepAsync(start.RunId!, 9_000, 9_100, 80_000, null, CancellationToken.None), Is.True);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(run.StepCount, Is.EqualTo(1));
            Assert.That(run.ToolCallCount, Is.Zero);
            Assert.That(run.PromptTokens, Is.Null);
            Assert.That(run.CompletionTokens, Is.Null);
            Assert.That(run.RequestBytes, Is.Null);
            Assert.That(run.EstimatedTokens, Is.EqualTo(9_100));
            Assert.That(run.MaxTokens, Is.EqualTo(80_000));
        });
    }

    [Test]
    public async Task AToolCallCountsWhenItIsAttemptedAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.RecordToolCallAsync(start.RunId!, CancellationToken.None), Is.True);
        Assert.That(await conversations.RecordToolCallAsync(start.RunId!, CancellationToken.None), Is.True);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        Assert.That(run.ToolCallCount, Is.EqualTo(2));
        Assert.That(await conversations.RecordToolCallAsync("no-such-run", CancellationToken.None), Is.False,
            "a call for a run that is not there counts nothing");
    }

    [Test]
    public async Task ANewRunStartsWithTheLastRunsCalibrationForTheSameModelAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult first = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.RecordStepAsync(first.RunId!, 4_000, 1_100, 100_000, new TokenUsage(1_000, 20), CancellationToken.None);
        await conversations.SetRunStatusAsync(first.RunId!, "completed");

        RunStartResult second = await conversations.StartRunAsync(conversation.Id, "owner", "again", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(second.RunId!, "completed");
        RunStartResult otherModel = await conversations.StartRunAsync(conversation.Id, "owner", "again", "other-model", "prompt-v1");

        Run copied = await database.Set<Run>().AsNoTracking().SingleAsync(run => run.Id == second.RunId);
        Run notCopied = await database.Set<Run>().AsNoTracking().SingleAsync(run => run.Id == otherModel.RunId);
        Assert.Multiple(() => {
            Assert.That(copied.RequestBytes, Is.EqualTo(4_000));
            Assert.That(copied.LastPromptTokens, Is.EqualTo(1_000));
            Assert.That(notCopied.RequestBytes, Is.Null, "a different model's ratio is not a safe start");
            Assert.That(notCopied.LastPromptTokens, Is.Null);
        });
    }

    [Test]
    public async Task DeletingAConversationKeepsItsMemoriesAndDropsTheirProvenanceAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        database.Add(new MemoryEntry {
            Owner = "owner",
            Kind = "fact",
            Content = "The site runs on Podman.",
            Version = 1,
            SourceConversationId = conversation.Id,
            SourceRunId = "run-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();

        Assert.That(await conversations.DeleteAsync(conversation.Id, "other"), Is.False);
        Assert.That((await database.Set<MemoryEntry>().AsNoTracking().SingleAsync()).SourceConversationId,
            Is.EqualTo(conversation.Id), "a refused delete changes nothing");

        Assert.That(await conversations.DeleteAsync(conversation.Id, "owner"), Is.True);

        MemoryEntry orphan = await database.Set<MemoryEntry>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(orphan.SourceConversationId, Is.Null);
            Assert.That(orphan.SourceRunId, Is.Null);
            Assert.That(orphan.Content, Is.EqualTo("The site runs on Podman."));
        });
    }

    [Test]
    public async Task AContinuationWidensOnlyTheBudgetTheOwnerWasShownAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);

        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Continuation, null, Continuation(RunBudget.Steps, 40, 40)), Is.True);
        Run paused = (await conversations.GetActiveRunAsync(conversation.Id, "owner"))!;
        Assert.Multiple(() => {
            Assert.That(paused.Status, Is.EqualTo(RunStatus.AwaitingOwner));
            Assert.That(paused.CompletedAt, Is.Null, "a paused run has not completed");
        });

        Assert.That(await conversations.ContinueRunAsync(conversation.Id, "owner", start.RunId!), Is.True);
        Run resumed = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;
        OwnerInterrupt interrupt = (await conversations.GetInterruptAsync(start.RunId!))!;
        Assert.Multiple(() => {
            Assert.That(resumed.Status, Is.EqualTo(RunStatus.Running));
            Assert.That(resumed.StepLimit, Is.EqualTo(80), "the steps ran out, so the steps grew by the window shown");
            Assert.That(resumed.ToolCallLimit, Is.Null, "the tool-call budget did not");
            Assert.That(resumed.CompletedAt, Is.Null);
            Assert.That(interrupt.Status, Is.EqualTo(OwnerInterruptStatus.Resolved));
            Assert.That(InterruptPayloads.Resolution(interrupt.ResolutionJson)?.Decision, Is.EqualTo("continue"));
        });
    }

    [Test]
    public async Task AToolCallPauseWidensTheCallBudgetAndNotTheStepsAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Continuation, null, Continuation(RunBudget.ToolCalls, 16, 16));

        Assert.That(await conversations.ContinueRunAsync(conversation.Id, "owner", start.RunId!), Is.True);
        Run resumed = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;
        Assert.Multiple(() => {
            Assert.That(resumed.ToolCallLimit, Is.EqualTo(32));
            Assert.That(resumed.StepLimit, Is.Null);
        });
    }

    [Test]
    public async Task OnlyAPausedRunOfTheOwnersConversationCanContinueAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);

        bool whileRunning = await conversations.ContinueRunAsync(conversation.Id, "owner", start.RunId!);
        await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Continuation, null, Continuation(RunBudget.Steps, 40, 40));
        bool byAnotherOwner = await conversations.ContinueRunAsync(conversation.Id, "someone", start.RunId!);
        bool inAnotherConversation = await conversations.ContinueRunAsync(conversation.Id + 1, "owner", start.RunId!);

        Run after = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;

        Assert.Multiple(() => {
            Assert.That(whileRunning, Is.False);
            Assert.That(byAnotherOwner, Is.False);
            Assert.That(inAnotherConversation, Is.False);
            Assert.That(after.Status, Is.EqualTo(RunStatus.AwaitingOwner), "a refused continuation leaves the run as it was");
        });
    }

    [Test]
    public async Task ARunWaitingOnItsOwnerStillOwnsTheConversationAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "first", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Continuation, null, Continuation(RunBudget.Steps, 40, 40));

        RunStartResult second = await conversations.StartRunAsync(conversation.Id, "owner", "second", "model", "prompt-v1");
        ConversationListItem listed = (await conversations.ListAsync("owner")).Single();

        Assert.Multiple(() => {
            Assert.That(second.Status, Is.EqualTo(RunStartStatus.AlreadyActive));
            Assert.That(listed.ActiveRunId, Is.EqualTo(start.RunId), "the list shows the waiting run as active");
        });
    }

    [Test]
    public async Task StartupRecoveryInterruptsARunWaitingOnItsOwnerAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Continuation, null, Continuation(RunBudget.Steps, 40, 40));

        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.EqualTo(1));

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        OwnerInterrupt interrupt = await database.Set<OwnerInterrupt>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo(RunStatus.Interrupted));
            Assert.That(interrupt.Status, Is.EqualTo(OwnerInterruptStatus.Failed),
                "the question died with the process that asked it");
        });
    }

    [Test]
    public async Task AQuestionIsAnsweredOnlyByAnOwnerWhoseRunIsWaitingAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "ask", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        Assert.That(await conversations.OpenInterruptAsync(
            start.RunId!, OwnerInterruptKind.Question, "call-1", Question()), Is.True);

        InterruptResolutionResult resolved = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Question, "call-1", Answers());

        IReadOnlyList<OwnerInterrupt> answered = await conversations.GetAnsweredQuestionsAsync(conversation.Id, "owner");
        IReadOnlyList<OwnerInterrupt> stranger = await conversations.GetAnsweredQuestionsAsync(conversation.Id, "someone");
        Run run = (await conversations.GetRunAsync(conversation.Id, "owner", start.RunId!))!;
        Assert.Multiple(() => {
            Assert.That(resolved, Is.EqualTo(InterruptResolutionResult.Resolved));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Running));
            Assert.That(answered, Has.Count.EqualTo(1));
            Assert.That(InterruptPayloads.Answers(answered[0].ResolutionJson)?.Answers[1].Text, Is.EqualTo("The first"));
            Assert.That(stranger, Is.Empty, "another account's conversation is not there at all");
        });
    }

    [Test]
    public async Task AnAnsweredQuestionCannotBeAnsweredTwiceAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "ask", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(start.RunId!, OwnerInterruptKind.Question, "call-1", Question());
        await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Question, "call-1", Answers());
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);

        InterruptResolutionResult again = await conversations.ResolveInterruptAsync(
            conversation.Id, "owner", start.RunId!, OwnerInterruptKind.Question, "call-1",
            OwnerQuestions.WriteAnswers(new OwnerAnswerSet([new OwnerAnswer("Which?", null, [], "Changed my mind")])));

        OwnerInterrupt stored = (await conversations.GetAnsweredQuestionsAsync(conversation.Id, "owner")).Single();
        Assert.Multiple(() => {
            Assert.That(again, Is.EqualTo(InterruptResolutionResult.Conflict));
            Assert.That(InterruptPayloads.Answers(stored.ResolutionJson)?.Answers[1].Text, Is.EqualTo("The first"),
                "the first answer is the one the record keeps");
        });
    }

    [Test]
    public async Task RecoveringARunFailsItsPendingQuestionAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "ask", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, RunStatus.Running);
        await conversations.OpenInterruptAsync(start.RunId!, OwnerInterruptKind.Question, "call-1", Question());

        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.EqualTo(1));

        OwnerInterrupt interrupt = await database.Set<OwnerInterrupt>().AsNoTracking().SingleAsync();
        IReadOnlyList<OwnerInterrupt> answered = await conversations.GetAnsweredQuestionsAsync(conversation.Id, "owner");
        Assert.Multiple(() => {
            Assert.That(interrupt.Status, Is.EqualTo(OwnerInterruptStatus.Failed));
            Assert.That(interrupt.FailureCode, Is.EqualTo("restart"));
            Assert.That(answered, Is.Empty,
                "a question the restart killed is not a card the transcript reads as answered");
        });
    }

    private static string Approval() =>
        InterruptPayloads.Write(new ApprovalRequest("write_page", "Writes a page.", "{}"));

    private static string Continuation(string budget, int limit, int window) =>
        InterruptPayloads.Write(new ContinuationRequest(budget, limit, window, RunBudget.Sentence(budget, limit, window)));

    private static string Question() => OwnerQuestions.Write(new OwnerQuestionSet([
        new OwnerQuestion("Which?", null, [new OwnerQuestionOption("Alpha", null), new OwnerQuestionOption("Beta", null)], false),
        new OwnerQuestion("Anything to add?", null, [], false),
    ]));

    private static string Answers() => OwnerQuestions.WriteAnswers(new OwnerAnswerSet([
        new OwnerAnswer("Which?", null, ["Alpha"], ""),
        new OwnerAnswer("Anything to add?", null, [], "The first"),
    ]));

    private static IConversationStore NewStore(StorageDbContext database) =>
        new SqliteConversationStore(database, TimeProvider.System, NullLogger<SqliteConversationStore>.Instance);
}
