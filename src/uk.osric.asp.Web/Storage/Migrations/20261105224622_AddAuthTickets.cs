// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using System;

/// <inheritdoc />
internal partial class AddAuthTickets : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
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

        migrationBuilder.CreateIndex(
            name: "IX_AuthTickets_ExpiresAt",
            table: "AuthTickets",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "AuthTickets");
    }
}
