// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// One record for every time a run waits on its owner: approvals and budget continuations move from
/// their two shapes into <c>AssistantInterrupts</c>, the two waiting statuses become one, and the
/// paused budget moves from a column into the continuation's stored request.
/// </summary>
internal partial class AddOwnerInterrupts : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "AssistantInterrupts",
            columns: table => new {
                RunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ToolCallId = table.Column<string>(type: "TEXT", nullable: true, collation: "BINARY"),
                RequestJson = table.Column<string>(type: "TEXT", nullable: false),
                ResolutionJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "BINARY"),
                FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_AssistantInterrupts", x => x.RunId);
                table.ForeignKey(
                    name: "FK_AssistantInterrupts_AssistantRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AssistantRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Every approval becomes an interrupt, so the owner's record survives the move: a settled one
        // keeps its decision, a failed one keeps its reason. The request an approval was shown was
        // only ever in the transcript, so it starts empty here; no pending one can outlive the deploy,
        // because the run it belongs to is interrupted below.
        migrationBuilder.Sql(
            """
            INSERT INTO "AssistantInterrupts"
                ("RunId", "Kind", "Status", "ToolCallId", "RequestJson", "ResolutionJson", "CreatedAt", "ResolvedAt", "ResolvedBy", "FailureCode")
            SELECT "RunId", 'approval',
                CASE "Status" WHEN 'pending' THEN 'pending' WHEN 'failed' THEN 'failed' ELSE 'resolved' END,
                "ToolCallId", '{}',
                CASE "Status"
                    WHEN 'approved' THEN '{"decision":"approve"}'
                    WHEN 'denied' THEN '{"decision":"deny"}'
                    ELSE NULL
                END,
                "CreatedAt", "ResolvedAt", "ResolvedBy", "FailureCode"
            FROM "AssistantToolApprovals";
            """);

        migrationBuilder.DropTable(
            name: "AssistantToolApprovals");

        // A run that was waiting on its owner when the deploy started is interrupted by it, exactly as
        // the host restart that follows would have interrupted it. Its interrupt keeps the request it
        // was showing, so the page can still say what the run stopped for.
        migrationBuilder.Sql(
            """
            UPDATE "AssistantRuns"
            SET "Status" = 'interrupted',
                "ErrorCode" = 'restart',
                "ErrorMessage" = 'The host restarted before the run completed.',
                "CompletedAt" = COALESCE("CompletedAt", "CreatedAt")
            WHERE "Status" IN ('awaiting_approval', 'awaiting_continuation');
            """);

        migrationBuilder.DropIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "PausedBudget",
            table: "AssistantRuns");

        migrationBuilder.CreateIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_owner')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns");

        migrationBuilder.AddColumn<string>(
            name: "PausedBudget",
            table: "AssistantRuns",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AssistantToolApprovals",
            columns: table => new {
                RunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                ToolCallId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "BINARY"),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AssistantToolApprovals", x => new { x.RunId, x.ToolCallId });
                table.ForeignKey(
                    name: "FK_AssistantToolApprovals_AssistantRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AssistantRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Every approval goes back to the table it came from, and only an approval can: a question or
        // a continuation has no row in the old shape, so the old build cannot show one. Runs waiting at
        // the time are put back the way the old build expects them, which is what the old page reads.
        migrationBuilder.Sql(
            """
            INSERT INTO "AssistantToolApprovals"
                ("RunId", "ToolCallId", "CreatedAt", "FailureCode", "ResolvedAt", "ResolvedBy", "Status")
            SELECT "RunId", COALESCE("ToolCallId", ''), "CreatedAt", "FailureCode", "ResolvedAt", "ResolvedBy",
                CASE
                    WHEN "Status" = 'pending' THEN 'pending'
                    WHEN "Status" = 'failed' THEN 'failed'
                    WHEN json_extract("ResolutionJson", '$.decision') = 'deny' THEN 'denied'
                    ELSE 'approved'
                END
            FROM "AssistantInterrupts"
            WHERE "Kind" = 'approval';
            """);

        migrationBuilder.Sql(
            """
            UPDATE "AssistantRuns"
            SET "Status" = CASE (
                    SELECT "Kind" FROM "AssistantInterrupts" AS i
                    WHERE i."RunId" = "AssistantRuns"."Id" AND i."Status" = 'pending')
                    WHEN 'approval' THEN 'awaiting_approval'
                    WHEN 'continuation' THEN 'awaiting_continuation'
                    ELSE "Status"
                END,
                "PausedBudget" = CASE (
                    SELECT "Kind" FROM "AssistantInterrupts" AS i
                    WHERE i."RunId" = "AssistantRuns"."Id" AND i."Status" = 'pending')
                    WHEN 'continuation' THEN (
                        SELECT json_extract(i."RequestJson", '$.budget') FROM "AssistantInterrupts" AS i
                        WHERE i."RunId" = "AssistantRuns"."Id")
                    ELSE NULL
                END
            WHERE "Status" = 'awaiting_owner';
            """);

        migrationBuilder.DropTable(
            name: "AssistantInterrupts");

        migrationBuilder.CreateIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_approval', 'awaiting_continuation')");
    }
}
