// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// A page's secrets: opaque text by name, owned by one page's subject, with no lifetime column because
/// a secret is the page's or it is not there.
/// </summary>
internal partial class AddStoreSecrets : Migration {

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "StoreSecrets",
            columns: table => new {
                Subject = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                PageKey = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Name = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Value = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => {
                table.PrimaryKey("PK_StoreSecrets", x => new { x.Subject, x.PageKey, x.Name });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "StoreSecrets");
    }
}
