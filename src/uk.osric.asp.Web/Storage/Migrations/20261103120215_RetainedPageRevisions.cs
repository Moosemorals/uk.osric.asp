// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

internal partial class RetainedPageRevisions : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "PageRevisions",
            columns: table => new {
                RevisionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Path = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Hash = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                SavedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                CreationRuntimeId = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                RuntimeId = table.Column<string>(type: "TEXT", nullable: true),
                Image = table.Column<byte[]>(type: "BLOB", nullable: true),
                Symbols = table.Column<byte[]>(type: "BLOB", nullable: true),
                Diagnostics = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table => {
                table.PrimaryKey("PK_PageRevisions", row => row.RevisionId);
                table.UniqueConstraint("AK_PageRevisions_RevisionId_Path", row => new { row.RevisionId, row.Path });
            });

        migrationBuilder.CreateTable(
            name: "PageHeads",
            columns: table => new {
                Path = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                RevisionId = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
            },
            constraints: table => {
                table.PrimaryKey("PK_PageHeads", row => row.Path);
                table.ForeignKey(
                    name: "FK_PageHeads_PageRevisions_RevisionId_Path",
                    columns: row => new { row.RevisionId, row.Path },
                    principalTable: "PageRevisions",
                    principalColumns: ["RevisionId", "Path"],
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql("""
            PRAGMA secure_delete=ON;
            CREATE TEMP TABLE "__PageRevisionBackfill" AS
            SELECT
                lower(hex(randomblob(16))) AS "RevisionId",
                "Path",
                strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now') AS "SavedAt",
                "Content",
                coalesce("RuntimeId", 'legacy:unknown') AS "CreationRuntimeId",
                "ContentHash",
                "RuntimeId",
                "Image",
                "Symbols",
                "Diagnostics"
            FROM "Files";

            INSERT INTO "PageRevisions" (
                "RevisionId", "Path", "Hash", "SavedAt", "Content", "CreationRuntimeId",
                "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
            SELECT
                "RevisionId", "Path",
                page_revision_hash("Content", "CreationRuntimeId", "SavedAt", "RevisionId"),
                "SavedAt", "Content", "CreationRuntimeId", "ContentHash", "RuntimeId",
                "Image", "Symbols", "Diagnostics"
            FROM "__PageRevisionBackfill";

            INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted")
            SELECT "Path", "RevisionId", 0 FROM "__PageRevisionBackfill";

            PRAGMA temp.secure_delete=ON;
            DROP TABLE "__PageRevisionBackfill";
            DROP TABLE "Files";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_PageHeads_RevisionId_Path",
            table: "PageHeads",
            columns: ["RevisionId", "Path"]);

        migrationBuilder.CreateIndex(
            name: "IX_PageRevisions_Hash",
            table: "PageRevisions",
            column: "Hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PageRevisions_Path",
            table: "PageRevisions",
            column: "Path");
    }

    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "Files",
            columns: table => new {
                Path = table.Column<string>(type: "TEXT", nullable: false, collation: "BINARY"),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                Diagnostics = table.Column<string>(type: "TEXT", nullable: true),
                Image = table.Column<byte[]>(type: "BLOB", nullable: true),
                RuntimeId = table.Column<string>(type: "TEXT", nullable: true),
                Symbols = table.Column<byte[]>(type: "BLOB", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_Files", row => row.Path));

        migrationBuilder.Sql("""
            INSERT INTO "Files" ("Path", "Content", "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
            SELECT revision."Path", revision."Content", revision."ContentHash", revision."RuntimeId",
                revision."Image", revision."Symbols", revision."Diagnostics"
            FROM "PageHeads" AS head
            JOIN "PageRevisions" AS revision
                ON revision."RevisionId" = head."RevisionId" AND revision."Path" = head."Path"
            WHERE head."IsDeleted" = 0;
            """);

        migrationBuilder.Sql("PRAGMA secure_delete=ON;");
        migrationBuilder.DropTable(name: "PageHeads");
        migrationBuilder.DropTable(name: "PageRevisions");
    }
}
