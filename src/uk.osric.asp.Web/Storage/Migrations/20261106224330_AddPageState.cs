// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <inheritdoc />
internal partial class AddPageState : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "PageState",
            columns: table => new {
                PagePath = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Key = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Value = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageState", x => new { x.PagePath, x.Key });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "PageState");
    }
}
