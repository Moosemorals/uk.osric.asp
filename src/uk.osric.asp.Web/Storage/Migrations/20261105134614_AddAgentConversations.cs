// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using System;

/// <inheritdoc />
internal partial class AddAgentConversations : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "AgentConversations",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                TitleIsGenerated = table.Column<bool>(type: "INTEGER", nullable: false),
                ApprovalMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                NextMessageSequence = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AgentConversations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AgentRuns",
            columns: table => new {
                Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ApprovalMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                PromptVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                StepCount = table.Column<int>(type: "INTEGER", nullable: false),
                ToolCallCount = table.Column<int>(type: "INTEGER", nullable: false),
                PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true),
                ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_AgentRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_AgentRuns_AgentConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "AgentConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AgentMessages",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ConversationId = table.Column<long>(type: "INTEGER", nullable: false),
                RunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                Reasoning = table.Column<string>(type: "TEXT", nullable: true),
                ToolCallsJson = table.Column<string>(type: "TEXT", nullable: true),
                ToolCallId = table.Column<string>(type: "TEXT", nullable: true, collation: "BINARY"),
                ToolName = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AgentMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_AgentMessages_AgentConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "AgentConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AgentMessages_AgentRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AgentRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AgentToolApprovals",
            columns: table => new {
                RunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                ToolCallId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "BINARY"),
                FailureCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_AgentToolApprovals", x => new { x.RunId, x.ToolCallId });
                table.ForeignKey(
                    name: "FK_AgentToolApprovals_AgentRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AgentRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "AgentMessages");

        migrationBuilder.DropTable(
            name: "AgentToolApprovals");

        migrationBuilder.DropTable(
            name: "AgentRuns");

        migrationBuilder.DropTable(
            name: "AgentConversations");
    }
}
