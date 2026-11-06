// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using System;

/// <inheritdoc />
public partial class InitialCreate : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "AssistantConversations",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                HasGeneratedTitle = table.Column<bool>(type: "INTEGER", nullable: false),
                ApprovalMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                NextMessageSequence = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AssistantConversations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AuthTickets",
            columns: table => new {
                Key = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "BINARY"),
                UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                ProtectedTicket = table.Column<string>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AuthTickets", x => x.Key);
            });

        migrationBuilder.CreateTable(
            name: "PageRevisions",
            columns: table => new {
                RevisionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Path = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Hash = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                SavedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Template = table.Column<string>(type: "TEXT", nullable: false),
                CreationRuntimeId = table.Column<string>(type: "TEXT", nullable: false),
                TemplateHash = table.Column<string>(type: "TEXT", nullable: true),
                RuntimeId = table.Column<string>(type: "TEXT", nullable: true),
                Image = table.Column<byte[]>(type: "BLOB", nullable: true),
                Symbols = table.Column<byte[]>(type: "BLOB", nullable: true),
                Diagnostics = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageRevisions", x => x.RevisionId);
                table.UniqueConstraint("AK_PageRevisions_RevisionId_Path", x => new { x.RevisionId, x.Path });
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new {
                UserName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Users", x => x.UserName);
            });

        migrationBuilder.CreateTable(
            name: "AssistantRuns",
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
                table.PrimaryKey("PK_AssistantRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssistantRuns_AssistantConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "AssistantConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PageHeads",
            columns: table => new {
                Path = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                RevisionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageHeads", x => x.Path);
                table.ForeignKey(
                    name: "FK_PageHeads_PageRevisions_RevisionId_Path",
                    columns: x => new { x.RevisionId, x.Path },
                    principalTable: "PageRevisions",
                    principalColumns: ["RevisionId", "Path"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AssistantMessages",
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
                table.PrimaryKey("PK_AssistantMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssistantMessages_AssistantConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "AssistantConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AssistantMessages_AssistantRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AssistantRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AssistantToolApprovals",
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
                table.PrimaryKey("PK_AssistantToolApprovals", x => new { x.RunId, x.ToolCallId });
                table.ForeignKey(
                    name: "FK_AssistantToolApprovals_AssistantRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "AssistantRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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

        migrationBuilder.CreateIndex(
            name: "IX_AuthTickets_ExpiresAt",
            table: "AuthTickets",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_PageHeads_RevisionId_Path",
            table: "PageHeads",
            columns: ["RevisionId", "Path"]);

        migrationBuilder.CreateIndex(
            name: "IX_PageRevisions_Hash",
            table: "PageRevisions",
            column: "Hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PageRevisions_Path",
            table: "PageRevisions",
            column: "Path");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "AssistantMessages");

        migrationBuilder.DropTable(
            name: "AssistantToolApprovals");

        migrationBuilder.DropTable(
            name: "AuthTickets");

        migrationBuilder.DropTable(
            name: "PageHeads");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "AssistantRuns");

        migrationBuilder.DropTable(
            name: "PageRevisions");

        migrationBuilder.DropTable(
            name: "AssistantConversations");
    }
}
