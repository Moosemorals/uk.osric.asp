// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <inheritdoc />
internal partial class AddRunUsage : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        // The old CompletionTokens column held the last step's figure. It becomes OutputTokens,
        // and CompletionTokens becomes the run's total across every step. Rows written before
        // this change only kept their last step, so the copied total is a lower bound.
        migrationBuilder.RenameColumn(
            name: "CompletionTokens",
            table: "AssistantRuns",
            newName: "OutputTokens");

        migrationBuilder.AddColumn<int>(
            name: "CompletionTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CachedTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "LastPromptTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReasoningTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "RequestBytes",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"AssistantRuns\" SET \"CompletionTokens\" = \"OutputTokens\", "
            + "\"LastPromptTokens\" = \"PromptTokens\";");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "CachedTokens",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "LastPromptTokens",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "ReasoningTokens",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "RequestBytes",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "CompletionTokens",
            table: "AssistantRuns");

        migrationBuilder.RenameColumn(
            name: "OutputTokens",
            table: "AssistantRuns",
            newName: "CompletionTokens");
    }
}
