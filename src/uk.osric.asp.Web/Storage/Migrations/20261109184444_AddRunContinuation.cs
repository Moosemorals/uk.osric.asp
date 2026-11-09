// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>A run that runs out of a budget waits for the owner instead of failing.</summary>
/// <remarks>
/// The three columns say which budget ran out and what the run's budgets have grown to, and the
/// active-run index grows the new status so a paused run still owns its conversation.
/// </remarks>
/// <inheritdoc />
internal partial class AddRunContinuation : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns");

        migrationBuilder.AddColumn<string>(
            name: "PausedBudget",
            table: "AssistantRuns",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StepLimit",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ToolCallLimit",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_approval', 'awaiting_continuation')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "PausedBudget",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "StepLimit",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "ToolCallLimit",
            table: "AssistantRuns");

        migrationBuilder.CreateIndex(
            name: "IX_AssistantRuns_ConversationId",
            table: "AssistantRuns",
            column: "ConversationId",
            unique: true,
            filter: "\"Status\" IN ('queued', 'running', 'awaiting_approval')");
    }
}
