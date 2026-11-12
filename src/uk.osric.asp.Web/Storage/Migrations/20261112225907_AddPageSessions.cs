// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>A visitor's session, and the values pages keep in it.</summary>
/// <remarks>
/// <c>PageSessions</c> carries the sliding timestamp and the account, and <c>PageSessionValues</c>
/// holds one row per value keyed by session, page and key, cascading from the session row. Two
/// indexes back the sweep and the eviction order: one on the account and timestamp, one on the
/// timestamp alone.
/// </remarks>
/// <inheritdoc />
internal partial class AddPageSessions : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "PageSessions",
            columns: table => new {
                SessionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Account = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "BINARY"),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageSessions", x => x.SessionId);
            });

        migrationBuilder.CreateTable(
            name: "PageSessionValues",
            columns: table => new {
                SessionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                PagePath = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Key = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Value = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageSessionValues", x => new { x.SessionId, x.PagePath, x.Key });
                table.ForeignKey(
                    name: "FK_PageSessionValues_PageSessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "PageSessions",
                    principalColumn: "SessionId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PageSessions_Account_UpdatedAt",
            table: "PageSessions",
            columns: ["Account", "UpdatedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_PageSessions_UpdatedAt",
            table: "PageSessions",
            column: "UpdatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "PageSessionValues");

        migrationBuilder.DropTable(
            name: "PageSessions");
    }
}
