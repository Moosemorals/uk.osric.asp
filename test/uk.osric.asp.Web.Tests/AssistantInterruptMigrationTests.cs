// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
[Category("Slow")]
public sealed class AssistantInterruptMigrationTests {

    private const string PreviousMigration = "20261118083447_AddRunEstimate";

    private string _databasePath = string.Empty;

    [SetUp]
    public void CreatePath() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"assistant-interrupt-migration-{Guid.NewGuid():N}.db");

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task TheMigrationCopiesApprovalsAndRetiresTheTwoWaitingStatusesAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(PreviousMigration);
        await SeedAsync(database);

        await database.Database.MigrateAsync();

        OwnerInterrupt approval = await database.Set<OwnerInterrupt>().AsNoTracking().SingleAsync();
        Run[] runs = await database.Set<Run>().AsNoTracking().OrderBy(run => run.Id).ToArrayAsync();
        Assert.Multiple(() => {
            Assert.That(SchemaNames(database), Does.Contain("AssistantInterrupts").And.Not.Contain("AssistantToolApprovals"));
            Assert.That(ColumnsOf(database, "AssistantRuns"), Does.Not.Contain("PausedBudget"));
            Assert.That(approval.Kind, Is.EqualTo(OwnerInterruptKind.Approval));
            Assert.That(approval.ToolCallId, Is.EqualTo("call-1"));
            Assert.That(approval.Status, Is.EqualTo(OwnerInterruptStatus.Resolved));
            Assert.That(InterruptPayloads.Resolution(approval.ResolutionJson)?.Decision, Is.EqualTo("approve"),
                "the decision the owner gave survives the move");
            Assert.That(runs.All(run => run.Status == RunStatus.Interrupted), "a waiting run is interrupted by the deploy");
            Assert.That(runs.All(run => run.ErrorCode == "restart"));
        });
    }

    [Test]
    public async Task TheDownMigrationRestoresTheApprovalsTableAndAPendingWaitAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync();
        await ExecAsync(database, """
            INSERT INTO "AssistantConversations"
                ("Id", "Owner", "Title", "HasGeneratedTitle", "ApprovalMode", "NextMessageSequence", "CreatedAt", "UpdatedAt")
            VALUES (1, 'owner', 'conversation', 0, 'RequireApproval', 2, '2026-11-18 12:00:00+00:00', '2026-11-18 12:00:00+00:00'),
                   (2, 'owner', 'conversation', 0, 'RequireApproval', 2, '2026-11-18 12:00:00+00:00', '2026-11-18 12:00:00+00:00');

            INSERT INTO "AssistantRuns"
                ("Id", "ConversationId", "Status", "Model", "ApprovalMode", "PromptVersion", "CreatedAt", "StepCount", "ToolCallCount")
            VALUES ('run-1', 1, 'completed', 'model', 'RequireApproval', 'prompt', '2026-11-18 12:00:00+00:00', 1, 1);

            INSERT INTO "AssistantRuns"
                ("Id", "ConversationId", "Status", "Model", "ApprovalMode", "PromptVersion", "CreatedAt", "StepCount", "ToolCallCount")
            VALUES ('run-2', 2, 'running', 'model', 'RequireApproval', 'prompt', '2026-11-18 12:00:00+00:00', 1, 1);

            INSERT INTO "AssistantInterrupts"
                ("RunId", "Kind", "Status", "ToolCallId", "RequestJson", "CreatedAt")
            VALUES ('run-1', 'approval', 'resolved', 'call-1', '{"name":"write_page","description":"Writes","arguments":"{}"}', '2026-11-18 12:00:00+00:00');

            UPDATE "AssistantInterrupts"
            SET "ResolutionJson" = '{"decision":"deny"}', "ResolvedAt" = '2026-11-18 12:00:01+00:00', "ResolvedBy" = 'owner'
            WHERE "RunId" = 'run-1';

            INSERT INTO "AssistantInterrupts"
                ("RunId", "Kind", "Status", "RequestJson", "CreatedAt")
            VALUES ('run-2', 'continuation', 'pending', '{"budget":"steps","limit":160,"window":160,"message":"The run has used all 160 of its steps."}', '2026-11-18 12:00:00+00:00');

            UPDATE "AssistantRuns" SET "Status" = 'awaiting_owner' WHERE "Id" = 'run-2';
            """);

        await database.Database.MigrateAsync(PreviousMigration);

        Assert.Multiple(() => {
            Assert.That(SchemaNames(database), Does.Contain("AssistantToolApprovals").And.Not.Contain("AssistantInterrupts"));
            Assert.That(ColumnsOf(database, "AssistantRuns"), Does.Contain("PausedBudget"));
            Assert.That(StatusOf(database, "run-1"), Is.EqualTo("completed"), "a settled approval leaves its run where it was");
            Assert.That(StatusOf(database, "run-2"), Is.EqualTo("awaiting_continuation"));
            Assert.That(PausedBudgetOf(database, "run-2"), Is.EqualTo("steps"),
                "the budget the owner was shown is the one the old page asks about");
            Assert.That(ApprovalStatusOf(database), Is.EqualTo("denied"));
        });
    }

    [Test]
    public async Task TheMigrationAppliesAgainOverItselfAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync();

        await database.Database.MigrateAsync(PreviousMigration);
        await database.Database.MigrateAsync();

        Assert.That(SchemaNames(database), Does.Contain("AssistantInterrupts"));
    }

    // The SQL carries JSON, whose braces EF's raw-string API reads as format items, so it goes
    // through the connection the way a migration would.
    private static async Task ExecAsync(StorageDbContext database, string sql) {
        await database.Database.OpenConnectionAsync();
        await using System.Data.Common.DbCommand command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync();
    }

    private static Task SeedAsync(StorageDbContext database) => ExecAsync(database, """
        INSERT INTO "AssistantConversations"
            ("Id", "Owner", "Title", "HasGeneratedTitle", "ApprovalMode", "NextMessageSequence", "CreatedAt", "UpdatedAt")
        VALUES (1, 'owner', 'conversation', 0, 'RequireApproval', 2, '2026-11-18 12:00:00+00:00', '2026-11-18 12:00:00+00:00'),
               (2, 'owner', 'conversation', 0, 'RequireApproval', 2, '2026-11-18 12:00:00+00:00', '2026-11-18 12:00:00+00:00');

        INSERT INTO "AssistantRuns"
            ("Id", "ConversationId", "Status", "Model", "ApprovalMode", "PromptVersion", "CreatedAt", "StepCount", "ToolCallCount", "PausedBudget")
        VALUES ('run-1', 1, 'awaiting_approval', 'model', 'RequireApproval', 'prompt', '2026-11-18 12:00:00+00:00', 1, 1, NULL);

        INSERT INTO "AssistantRuns"
            ("Id", "ConversationId", "Status", "Model", "ApprovalMode", "PromptVersion", "CreatedAt", "StepCount", "ToolCallCount", "PausedBudget")
        VALUES ('run-2', 2, 'awaiting_continuation', 'model', 'RequireApproval', 'prompt', '2026-11-18 12:00:00+00:00', 0, 0, 'steps');

        INSERT INTO "AssistantToolApprovals"
            ("RunId", "ToolCallId", "Status", "CreatedAt", "ResolvedAt", "ResolvedBy")
        VALUES ('run-1', 'call-1', 'approved', '2026-11-18 12:00:00+00:00', '2026-11-18 12:00:01+00:00', 'owner');
        """);

    private static string[] SchemaNames(StorageDbContext database) => [.. database.Database
        .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM sqlite_master WHERE \"type\" = 'table'")
        .ToArray()];

    private static string[] ColumnsOf(StorageDbContext database, string table) => [.. database.Database
        .SqlQuery<string>($"SELECT \"name\" AS \"Value\" FROM pragma_table_info({table})")
        .ToArray()];

    private static string? StatusOf(StorageDbContext database, string runId) => database.Database
        .SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM \"AssistantRuns\" WHERE \"Id\" = {runId}")
        .ToArray()[0];

    private static string? PausedBudgetOf(StorageDbContext database, string runId) => database.Database
        .SqlQuery<string?>($"SELECT \"PausedBudget\" AS \"Value\" FROM \"AssistantRuns\" WHERE \"Id\" = {runId}")
        .ToArray()[0];

    private static string? ApprovalStatusOf(StorageDbContext database) => database.Database
        .SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM \"AssistantToolApprovals\"")
        .ToArray()[0];
}
