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
        database.Add(new ToolApproval {
            RunId = runId,
            ToolCallId = "call-1",
            Status = "executed",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();

        Assert.That(await conversations.DeleteAsync(conversation.Id, "owner"), Is.True);
        Assert.Multiple(() => {
            Assert.That(database.Set<Conversation>().Count(), Is.Zero);
            Assert.That(database.Set<Run>().Count(), Is.Zero);
            Assert.That(database.Set<Message>().Count(), Is.Zero);
            Assert.That(database.Set<ToolApproval>().Count(), Is.Zero);
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

        long?[] sequences = await Task.WhenAll(appends);
        await using StorageDbContext reader = new(_options);
        IReadOnlyList<Message> messages = await NewStore(reader).GetMessagesAsync(conversationId, "owner");

        Assert.That(sequences, Is.All.Not.Null);
        Assert.That(messages.Select(message => message.Sequence), Is.EqualTo(Enumerable.Range(1, Appends + 1).Select(value => (long)value)));
    }

    [Test]
    public async Task ApprovalResolvesOnlyWhilePendingForTheOwnedActiveRunAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        Assert.That(await conversations.CreateApprovalAsync(start.RunId!, "call-1"), Is.True);

        ApprovalResolutionResult approved = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "approve");
        ApprovalResolutionResult repeated = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "approve");
        ApprovalResolutionResult conflicting = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "deny");

        Assert.That(approved, Is.EqualTo(ApprovalResolutionResult.Approved));
        Assert.That(repeated, Is.EqualTo(ApprovalResolutionResult.Approved));
        Assert.That(conflicting, Is.EqualTo(ApprovalResolutionResult.Conflict));
        Assert.That(await conversations.GetPendingApprovalsAsync(conversation.Id, "owner"), Is.Empty);
    }

    [Test]
    public async Task StoppingRunFailsPendingApprovalsWithoutExecutingThemAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        await conversations.CreateApprovalAsync(start.RunId!, "call-1");

        Assert.That(await conversations.FailPendingApprovalsAsync(start.RunId!, "stop"), Is.EqualTo(1));

        ToolApproval approval = (await database.Set<ToolApproval>().AsNoTracking().SingleAsync())!;
        Assert.That(approval.Status, Is.EqualTo("failed"));
        Assert.That(approval.FailureCode, Is.EqualTo("stop"));
        Assert.That(await conversations.ResolveApprovalAsync(conversation.Id, "owner", start.RunId!, "call-1", "approve"),
            Is.EqualTo(ApprovalResolutionResult.Conflict));
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
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        await conversations.CreateApprovalAsync(start.RunId!, "call-2");

        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.EqualTo(1));
        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.Zero);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        ToolApproval approval = await database.Set<ToolApproval>().AsNoTracking().SingleAsync();
        Message repaired = await database.Set<Message>().AsNoTracking()
            .SingleAsync(message => message.ToolCallId == "call-2");
        Assert.Multiple(() => {
            Assert.That(run.Status, Is.EqualTo("interrupted"));
            Assert.That(run.ErrorCode, Is.EqualTo("restart"));
            Assert.That(approval.Status, Is.EqualTo("failed"));
            Assert.That(approval.FailureCode, Is.EqualTo("restart"));
            Assert.That(repaired.Content, Does.Contain("host restarted"));
            Assert.That(repaired.ToolName, Is.EqualTo("delete_page"));
        });
    }

    [Test]
    public async Task RecordStepAddsEveryReportedCountToTheRunAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.RecordStepAsync(start.RunId!, 1, 4_000,
            new TokenUsage(1_000, 20, 600, 5), CancellationToken.None), Is.True);
        Assert.That(await conversations.RecordStepAsync(start.RunId!, 2, 5_000,
            new TokenUsage(2_000, 30, 1_200, 7), CancellationToken.None), Is.True);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(run.StepCount, Is.EqualTo(2));
            Assert.That(run.ToolCallCount, Is.EqualTo(3));
            Assert.That(run.PromptTokens, Is.EqualTo(3_000));
            Assert.That(run.CompletionTokens, Is.EqualTo(50));
            Assert.That(run.CachedTokens, Is.EqualTo(1_800));
            Assert.That(run.ReasoningTokens, Is.EqualTo(12));
            Assert.That(run.OutputTokens, Is.EqualTo(30), "the output budget counts the last step");
            Assert.That(run.RequestBytes, Is.EqualTo(5_000));
            Assert.That(run.LastPromptTokens, Is.EqualTo(2_000));
        });
    }

    [Test]
    public async Task ANullUsageStillCountsTheStepAndLeavesTheTotalsAloneAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");

        Assert.That(await conversations.RecordStepAsync(start.RunId!, 4, 9_000, null, CancellationToken.None), Is.True);

        Run run = await database.Set<Run>().AsNoTracking().SingleAsync();
        Assert.Multiple(() => {
            Assert.That(run.StepCount, Is.EqualTo(1));
            Assert.That(run.ToolCallCount, Is.EqualTo(4));
            Assert.That(run.PromptTokens, Is.Null);
            Assert.That(run.CompletionTokens, Is.Null);
            Assert.That(run.RequestBytes, Is.Null);
        });
    }

    [Test]
    public async Task ANewRunStartsWithTheLastRunsCalibrationForTheSameModelAsync() {
        await using StorageDbContext database = new(_options);
        IConversationStore conversations = NewStore(database);
        Conversation conversation = await conversations.CreateAsync("owner");
        RunStartResult first = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.RecordStepAsync(first.RunId!, 0, 4_000, new TokenUsage(1_000, 20), CancellationToken.None);
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

    private static IConversationStore NewStore(StorageDbContext database) =>
        new SqliteConversationStore(database, TimeProvider.System, NullLogger<SqliteConversationStore>.Instance);
}
