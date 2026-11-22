// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// One value table for both subjects: <c>PageState</c> and <c>PageSessionValues</c> become
/// <c>StoreValues</c>, with the subject in the key and the lifetime on the row, and both old tables
/// are dropped after their rows have moved.
/// </summary>
internal partial class AddStoreValues : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        // The table exists before the copy and the drop comes after it, in one transaction: a crash
        // leaves either the old shape or the new one, never a half-copied store.
        migrationBuilder.CreateTable(
            name: "StoreValues",
            columns: table => new {
                Subject = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                PageKey = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Key = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Lifetime = table.Column<int>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<string>(type: "TEXT", nullable: true),
                Value = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_StoreValues", x => new { x.Subject, x.PageKey, x.Key });
            });

        migrationBuilder.CreateIndex(
            name: "IX_StoreValues_ExpiresAt",
            table: "StoreValues",
            column: "ExpiresAt");

        // A page's own state becomes its Page subject with the page's lifetime.
        migrationBuilder.Sql(
            """
            INSERT INTO "StoreValues" ("Subject", "PageKey", "Key", "Lifetime", "ExpiresAt", "Value", "UpdatedAt")
            SELECT 'page:' || "PagePath", "PagePath", "Key", 1, NULL, "Value",
                   strftime('%Y-%m-%dT%H:%M:%f', "UpdatedAt") || '0000Z'
            FROM "PageState";
            """);

        // A session's values become its session subject with the Session lifetime.
        migrationBuilder.Sql(
            """
            INSERT INTO "StoreValues" ("Subject", "PageKey", "Key", "Lifetime", "ExpiresAt", "Value", "UpdatedAt")
            SELECT 'session:' || value."SessionId", value."PagePath", value."Key", 3, NULL, value."Value",
                   strftime('%Y-%m-%dT%H:%M:%f', session."UpdatedAt") || '0000Z'
            FROM "PageSessionValues" AS value
            JOIN "PageSessions" AS session ON session."SessionId" = value."SessionId";
            """);

        migrationBuilder.DropTable(
            name: "PageSessionValues");

        migrationBuilder.DropTable(
            name: "PageState");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
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

        migrationBuilder.CreateTable(
            name: "PageState",
            columns: table => new {
                PagePath = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Key = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_PageState", x => new { x.PagePath, x.Key });
            });

        // Only the two lifetimes the old schema had can come back; a Person, Browser, NextRead or
        // Until row did not exist before this migration and is not promised a round trip.
        migrationBuilder.Sql(
            """
            INSERT INTO "PageState" ("PagePath", "Key", "UpdatedAt", "Value")
            SELECT "PageKey", "Key", datetime("UpdatedAt"), "Value"
            FROM "StoreValues"
            WHERE "Lifetime" = 1 AND "ExpiresAt" IS NULL;
            """);

        migrationBuilder.Sql(
            """
            INSERT INTO "PageSessionValues" ("SessionId", "PagePath", "Key", "Value")
            SELECT substr("Subject", length('session:') + 1), "PageKey", "Key", "Value"
            FROM "StoreValues"
            WHERE "Lifetime" = 3 AND "ExpiresAt" IS NULL
              AND "Subject" LIKE 'session:%'
              AND substr("Subject", length('session:') + 1) IN (SELECT "SessionId" FROM "PageSessions");
            """);

        migrationBuilder.DropTable(
            name: "StoreValues");
    }
}
