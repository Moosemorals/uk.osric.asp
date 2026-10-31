// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using System;

/// <inheritdoc />
public partial class AddLocalUsers : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "Users");
    }
}
