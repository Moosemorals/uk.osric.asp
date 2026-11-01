// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

internal partial class AddCompiledPageColumns : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<string>(
            name: "ContentHash",
            table: "Files",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Diagnostics",
            table: "Files",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "Image",
            table: "Files",
            type: "BLOB",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RuntimeId",
            table: "Files",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "ContentHash",
            table: "Files");

        migrationBuilder.DropColumn(
            name: "Diagnostics",
            table: "Files");

        migrationBuilder.DropColumn(
            name: "Image",
            table: "Files");

        migrationBuilder.DropColumn(
            name: "RuntimeId",
            table: "Files");
    }
}
