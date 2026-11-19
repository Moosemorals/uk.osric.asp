// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Keeps the last request's estimate and reply allowance with the run.</summary>
/// <remarks>
/// The estimate is what the panel's signed error row is measured from, and the allowance is the
/// ceiling the completion row is read against. Both are null for every row that exists, which is
/// right: a run written before this change recorded neither.
/// </remarks>
internal partial class AddRunEstimate : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<long>(
            name: "EstimatedTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "MaxTokens",
            table: "AssistantRuns",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "EstimatedTokens",
            table: "AssistantRuns");

        migrationBuilder.DropColumn(
            name: "MaxTokens",
            table: "AssistantRuns");
    }
}
