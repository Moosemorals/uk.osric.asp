// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// The catalogue a source's declared tables are found through: one row per table, holding what the
/// source declared when it was applied and the unguessable name the table really has.
/// </summary>
/// <remarks>
/// The declared tables themselves are not part of this migration: they are made by the save that
/// declares them, inside its own transaction. Its <c>Down</c> therefore drops the catalogue and leaves
/// those tables alone, because a migration cannot enumerate a name it cannot see and must not guess at
/// one; a scrub is what finds and removes what an older build left behind.
/// </remarks>
internal partial class AddDeclaredTables : Migration {

    private static readonly string[] Columns = ["SourceKey", "Name"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "DeclaredTables",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SourceKey = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Name = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Subject = table.Column<int>(type: "INTEGER", nullable: false),
                Lifetime = table.Column<int>(type: "INTEGER", nullable: false),
                Columns = table.Column<string>(type: "TEXT", nullable: false),
                Search = table.Column<string>(type: "TEXT", nullable: true),
                RealName = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_DeclaredTables", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeclaredTables_SourceKey_Name",
            table: "DeclaredTables",
            columns: Columns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "DeclaredTables");
    }
}
