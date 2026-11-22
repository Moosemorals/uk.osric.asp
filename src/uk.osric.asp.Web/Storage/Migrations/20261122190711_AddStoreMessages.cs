// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// The messages a visitor's pages leave for each other: one row per message, addressed to a page or
/// a topic, filtered to the reader by its subject, and taken once.
/// </summary>
internal partial class AddStoreMessages : Migration {

    private static readonly string[] AddressAndOrder = ["Subject", "ToPageKey", "Id"];

    private static readonly string[] TopicAndOrder = ["Subject", "Topic", "Id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "StoreMessages",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Subject = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                ToPageKey = table.Column<string>(type: "TEXT", nullable: true, collation: "BINARY"),
                Topic = table.Column<string>(type: "TEXT", nullable: true, collation: "BINARY"),
                FromPageKey = table.Column<string>(type: "TEXT", nullable: false),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                Lifetime = table.Column<int>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<string>(type: "TEXT", nullable: true),
                SentAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_StoreMessages", x => x.Id);
                table.CheckConstraint("CK_StoreMessages_Address", "(\"ToPageKey\" IS NULL) <> (\"Topic\" IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "IX_StoreMessages_ExpiresAt",
            table: "StoreMessages",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_StoreMessages_Subject_ToPageKey_Id",
            table: "StoreMessages",
            columns: AddressAndOrder);

        migrationBuilder.CreateIndex(
            name: "IX_StoreMessages_Subject_Topic_Id",
            table: "StoreMessages",
            columns: TopicAndOrder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(
            name: "StoreMessages");
    }
}
