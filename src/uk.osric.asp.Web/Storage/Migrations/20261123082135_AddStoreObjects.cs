// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// The bytes a subject stores, addressed by their SHA-256 digest: one row per subject and digest, so
/// the same bytes put twice are one row.
/// </summary>
internal partial class AddStoreObjects : Migration {

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "StoreObjects",
            columns: table => new {
                Subject = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Sha = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Length = table.Column<long>(type: "INTEGER", nullable: false),
                ContentType = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Lifetime = table.Column<int>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<string>(type: "TEXT", nullable: true),
                Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => {
                table.PrimaryKey("PK_StoreObjects", x => new { x.Subject, x.Sha });
            });

        migrationBuilder.CreateIndex(
            name: "IX_StoreObjects_ExpiresAt",
            table: "StoreObjects",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_StoreObjects_Sha",
            table: "StoreObjects",
            column: "Sha");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "StoreObjects");
    }
}
