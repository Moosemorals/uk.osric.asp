// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Core.Agent;
using uk.osric.asp.Web.Agent;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AgentConversationStoreTests {

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"agent-conversations-{Guid.NewGuid():N}.db");
        SqliteConnectionStringBuilder connection = new() { DataSource = _databasePath, Pooling = false };
        _options = new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(connection.ToString()).Options;
        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();
    }

    [TearDown]
    public void TearDown() {
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    public async Task NewConversationUsesGeneratedTitleAndAlwaysApproveAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);

        AgentConversation created = await conversations.CreateAsync("owner");

        Assert.Multiple(() => {
            Assert.That(created.Title, Is.EqualTo("New conversation"));
            Assert.That(created.TitleIsGenerated, Is.True);
            Assert.That(created.ApprovalMode, Is.EqualTo("AlwaysApprove"));
            Assert.That(created.NextMessageSequence, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConversationReadsAndListsAreScopedToTheOwnerAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation first = await conversations.CreateAsync("owner");
        AgentConversation other = await conversations.CreateAsync("other");

        Assert.That(await conversations.GetAsync(other.Id, "owner"), Is.Null);
        Assert.That((await conversations.ListAsync("owner")).Select(item => item.Id), Is.EqualTo([first.Id]));
    }

    [Test]
    public async Task RenameAndApprovalModeUpdateOnlyTheOwnedConversationAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");

        Assert.That(await conversations.RenameAsync(conversation.Id, "owner", "My pages"), Is.True);
        Assert.That(await conversations.SetApprovalModeAsync(conversation.Id, "owner", AgentApprovalMode.RequireApproval), Is.True);
        Assert.That(await conversations.RenameAsync(conversation.Id, "other", "No"), Is.False);

        AgentConversation saved = (await conversations.GetAsync(conversation.Id, "owner"))!;
        Assert.Multiple(() => {
            Assert.That(saved.Title, Is.EqualTo("My pages"));
            Assert.That(saved.TitleIsGenerated, Is.False);
            Assert.That(saved.ApprovalMode, Is.EqualTo("RequireApproval"));
        });
    }

    [Test]
    public async Task DeletingConversationCascadesThroughMessagesRunsAndApprovalsAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        string runId = Guid.NewGuid().ToString("N");
        database.Add(new AgentRun {
            Id = runId,
            ConversationId = conversation.Id,
            Status = "completed",
            Model = "test",
            ApprovalMode = "AlwaysApprove",
            PromptVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            StepCount = 1,
        });
        database.Add(new AgentMessage {
            ConversationId = conversation.Id,
            RunId = runId,
            Sequence = 1,
            Role = "user",
            Content = "message",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        database.Add(new AgentToolApproval {
            RunId = runId,
            ToolCallId = "call-1",
            Status = "executed",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();

        Assert.That(await conversations.DeleteAsync(conversation.Id, "owner"), Is.True);
        Assert.Multiple(() => {
            Assert.That(database.Set<AgentConversation>().Count(), Is.Zero);
            Assert.That(database.Set<AgentRun>().Count(), Is.Zero);
            Assert.That(database.Set<AgentMessage>().Count(), Is.Zero);
            Assert.That(database.Set<AgentToolApproval>().Count(), Is.Zero);
        });
    }

    [Test]
    public async Task StartingRunStoresTheUserMessageAndApprovalSnapshotInOneTransactionAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        await conversations.SetApprovalModeAsync(conversation.Id, "owner", AgentApprovalMode.RequireApproval);

        AgentRunStartResult started = await conversations.StartRunAsync(
            conversation.Id, "owner", "Build a page", "model", "prompt-v1");

        Assert.That(started.Status, Is.EqualTo(AgentRunStartStatus.Started));
        Assert.That(started.UserMessageSequence, Is.EqualTo(1));
        Assert.That(started.RunId, Is.Not.Null);
        AgentRun run = (await conversations.GetActiveRunAsync(conversation.Id, "owner"))!;
        AgentMessage userMessage = (await conversations.GetMessagesAsync(conversation.Id, "owner")).Single();
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
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        AgentRunStartResult first = await conversations.StartRunAsync(conversation.Id, "owner", "First request", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(first.RunId!, "completed");
        AgentRunStartResult second = await conversations.StartRunAsync(conversation.Id, "owner", "Another request", "model", "prompt-v1");

        Assert.That(second.Status, Is.EqualTo(AgentRunStartStatus.Started));
        Assert.That((await conversations.GetAsync(conversation.Id, "owner"))?.Title, Is.EqualTo("First request"));
    }

    [Test]
    public async Task SecondActiveRunIsRejectedWithoutAllocatingAnotherMessageSequenceAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        await conversations.StartRunAsync(conversation.Id, "owner", "first", "model", "prompt-v1");

        AgentRunStartResult second = await conversations.StartRunAsync(
            conversation.Id, "owner", "second", "model", "prompt-v1");

        AgentConversation saved = (await conversations.GetAsync(conversation.Id, "owner"))!;
        Assert.That(second.Status, Is.EqualTo(AgentRunStartStatus.AlreadyActive));
        Assert.That(saved.NextMessageSequence, Is.EqualTo(2));
        Assert.That((await conversations.GetMessagesAsync(conversation.Id, "owner")).Select(message => message.Content), Is.EqualTo(["first"]));
    }

    [Test]
    public async Task ConcurrentTranscriptAppendsReceiveUniqueSequencesAsync() {
        long conversationId;
        string runId;
        await using (StorageDbContext database = new(_options)) {
            AgentConversation conversation = await NewStore(database).CreateAsync("owner");
            AgentRunStartResult start = await NewStore(database).StartRunAsync(
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
                conversationId, runId, new AgentMessageInput("assistant", $"reply-{index}"));
        })];
        gate.SetResult();

        long?[] sequences = await Task.WhenAll(appends);
        await using StorageDbContext reader = new(_options);
        IReadOnlyList<AgentMessage> messages = await NewStore(reader).GetMessagesAsync(conversationId, "owner");

        Assert.That(sequences, Is.All.Not.Null);
        Assert.That(messages.Select(message => message.Sequence), Is.EqualTo(Enumerable.Range(1, Appends + 1).Select(value => (long)value)));
    }

    [Test]
    public async Task ApprovalResolvesOnlyWhilePendingForTheOwnedActiveRunAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        AgentRunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        Assert.That(await conversations.CreateApprovalAsync(start.RunId!, "call-1"), Is.True);

        AgentApprovalResolutionResult approved = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "approve");
        AgentApprovalResolutionResult repeated = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "approve");
        AgentApprovalResolutionResult conflicting = await conversations.ResolveApprovalAsync(
            conversation.Id, "owner", start.RunId!, "call-1", "deny");

        Assert.That(approved, Is.EqualTo(AgentApprovalResolutionResult.Approved));
        Assert.That(repeated, Is.EqualTo(AgentApprovalResolutionResult.Approved));
        Assert.That(conflicting, Is.EqualTo(AgentApprovalResolutionResult.Conflict));
        Assert.That(await conversations.GetPendingApprovalsAsync(conversation.Id, "owner"), Is.Empty);
    }

    [Test]
    public async Task StoppingRunFailsPendingApprovalsWithoutExecutingThemAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        AgentRunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        await conversations.CreateApprovalAsync(start.RunId!, "call-1");

        Assert.That(await conversations.FailPendingApprovalsAsync(start.RunId!, "stop"), Is.EqualTo(1));

        AgentToolApproval approval = (await database.Set<AgentToolApproval>().AsNoTracking().SingleAsync())!;
        Assert.That(approval.Status, Is.EqualTo("failed"));
        Assert.That(approval.FailureCode, Is.EqualTo("stop"));
        Assert.That(await conversations.ResolveApprovalAsync(conversation.Id, "owner", start.RunId!, "call-1", "approve"),
            Is.EqualTo(AgentApprovalResolutionResult.Conflict));
    }

    [Test]
    public async Task StartupRecoveryInterruptsRunsAndRepairsDanglingToolCallsAsync() {
        await using StorageDbContext database = new(_options);
        IAgentConversationStore conversations = NewStore(database);
        AgentConversation conversation = await conversations.CreateAsync("owner");
        AgentRunStartResult start = await conversations.StartRunAsync(conversation.Id, "owner", "write", "model", "prompt-v1");
        string toolCalls = "[{\"id\":\"call-1\",\"type\":\"function\",\"function\":{\"name\":\"write_page\",\"arguments\":\"{}\"}},{\"id\":\"call-2\",\"type\":\"function\",\"function\":{\"name\":\"delete_page\",\"arguments\":\"{}\"}}]";
        await conversations.AppendMessageAsync(conversation.Id, start.RunId!, new AgentMessageInput("assistant", "", ToolCallsJson: toolCalls));
        await conversations.AppendMessageAsync(conversation.Id, start.RunId!, new AgentMessageInput("tool", "done", ToolCallId: "call-1", ToolName: "write_page"));
        await conversations.SetRunStatusAsync(start.RunId!, "awaiting_approval");
        await conversations.CreateApprovalAsync(start.RunId!, "call-2");

        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.EqualTo(1));
        Assert.That(await conversations.RecoverInterruptedRunsAsync(), Is.Zero);

        AgentRun run = await database.Set<AgentRun>().AsNoTracking().SingleAsync();
        AgentToolApproval approval = await database.Set<AgentToolApproval>().AsNoTracking().SingleAsync();
        AgentMessage repaired = await database.Set<AgentMessage>().AsNoTracking()
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

    private static IAgentConversationStore NewStore(StorageDbContext database) =>
        new SqliteAgentConversationStore(database, TimeProvider.System);
}
