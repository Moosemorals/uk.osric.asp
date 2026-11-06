// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// Renames the assistant's tables to match the types that own them. The scaffolded drop and create was
/// replaced with renames so conversations, runs, transcripts and approvals survive the upgrade; the
/// indexes are dropped and recreated because SQLite cannot rename one in place, which rebuilds them
/// without touching a row.
/// </summary>
internal partial class RenameAssistantTables : Migration {

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_AgentConversations_Owner_UpdatedAt",
            table: "AgentConversations");

        migrationBuilder.DropIndex(
            name: "IX_AgentMessages_ConversationId_RunId",
            table: "AgentMessages");

        migrationBuilder.DropIndex(
            name: "IX_AgentMessages_ConversationId_Sequence",
            table: "AgentMessages");

        migrationBuilder.DropIndex(
            name: "IX_AgentMessages_RunId",
            table: "AgentMessages");

        migrationBuilder.DropIndex(
            name: "IX_AgentRuns_ConversationId",
            table: "AgentRuns");

        migrationBuilder.RenameTable(
            name: "AgentConversations",
            newName: "AssistantConversations");

        migrationBuilder.RenameTable(
            name: "AgentRuns",
            newName: "AssistantRuns");

        migrationBuilder.RenameTable(
            name: "AgentMessages",
            newName: "AssistantMessages");

        migrationBuilder.RenameTable(
            name: "AgentToolApprovals",
            newName: "AssistantToolApprovals");

        migrationBuilder.CreateIndex(
            name: "IX_AssistantConversations_Owner_UpdatedAt",
            table: "AssistantConversations",
            columns: ["Owner", "UpdatedAt"],
            descending: [false, true]);

        migrationBuilder.CreateIndex(
            name: "IX_AssistantMessages_ConversationId_RunId",
            table: "AssistantMessages",
            columns: ["ConversationId", "RunId"]);

        migrationBuilder.CreateIndex(
            name: "IX_AssistantMessages_ConversationId_Sequence",
            table: "AssistantMessages",
            columns: ["ConversationId", "Sequence"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssistantMessages_RunId",
            table: "AssistantMessages",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_approval')");

    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_AssistantConversations_Owner_UpdatedAt",
            table: "AssistantConversations");

        migrationBuilder.DropIndex(
            name: "IX_AssistantMessages_ConversationId_RunId",
            table: "AssistantMessages");

        migrationBuilder.DropIndex(
            name: "IX_AssistantMessages_ConversationId_Sequence",
            table: "AssistantMessages");

        migrationBuilder.DropIndex(
            name: "IX_AssistantMessages_RunId",
            table: "AssistantMessages");

        migrationBuilder.DropIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns");

        migrationBuilder.RenameTable(
            name: "AssistantConversations",
            newName: "AgentConversations");

        migrationBuilder.RenameTable(
            name: "AssistantRuns",
            newName: "AgentRuns");

        migrationBuilder.RenameTable(
            name: "AssistantMessages",
            newName: "AgentMessages");

        migrationBuilder.RenameTable(
            name: "AssistantToolApprovals",
            newName: "AgentToolApprovals");

        migrationBuilder.CreateIndex(
            name: "IX_AgentConversations_Owner_UpdatedAt",
            table: "AgentConversations",
            columns: ["Owner", "UpdatedAt"],
            descending: [false, true]);

        migrationBuilder.CreateIndex(
            name: "IX_AgentMessages_ConversationId_RunId",
            table: "AgentMessages",
            columns: ["ConversationId", "RunId"]);

        migrationBuilder.CreateIndex(
            name: "IX_AgentMessages_ConversationId_Sequence",
            table: "AgentMessages",
            columns: ["ConversationId", "Sequence"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AgentMessages_RunId",
            table: "AgentMessages",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_AgentRuns_ConversationId",
            table: "AgentRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_approval')");

    }
}
