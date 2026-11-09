// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>The assistant's memory: two ordinary tables, an index, and an FTS5 index over the heads.</summary>
/// <remarks>
/// EF Core cannot model a virtual table, so the FTS5 table and its three triggers are raw SQL in the
/// same migration and the model snapshot does not mention them. That is also why <see cref="Down"/>
/// drops them by hand: the generated reversal only knows the two tables. The DDL is the text the
/// spike ran and checked with FTS5's own <c>integrity-check</c>.
/// </remarks>
internal partial class AddAssistantMemory : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "AssistantMemories",
            columns: table => new {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, collation: "BINARY"),
                Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false),
                Pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                SourceConversationId = table.Column<long>(type: "INTEGER", nullable: true),
                SourceRunId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true, collation: "BINARY"),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastRecalledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LastRecalledConversationId = table.Column<long>(type: "INTEGER", nullable: true),
                RecallCount = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_AssistantMemories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AssistantMemoryEdits",
            columns: table => new {
                MemoryId = table.Column<long>(type: "INTEGER", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                EditedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                SourceConversationId = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_AssistantMemoryEdits", x => new { x.MemoryId, x.Version });
                table.ForeignKey(
                    name: "FK_AssistantMemoryEdits_AssistantMemories_MemoryId",
                    column: x => x.MemoryId,
                    principalTable: "AssistantMemories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssistantMemories_Owner",
            table: "AssistantMemories",
            columns: ["Owner", "UpdatedAt"],
            descending: [false, true]);

        // External content: the index stores no copy of a memory's text, it reads the row by Id. The
        // Owner and Kind columns are unindexed so a query can filter on them through the index, and the
        // update trigger names its columns so a per-recall touch does not reindex the row.
        migrationBuilder.Sql("""
            CREATE VIRTUAL TABLE "AssistantMemorySearch" USING fts5(
                "Content",
                "Owner" UNINDEXED,
                "Kind"  UNINDEXED,
                content='AssistantMemories',
                content_rowid='Id',
                tokenize='porter unicode61 remove_diacritics 2'
            );

            CREATE TRIGGER "AssistantMemories_ai" AFTER INSERT ON "AssistantMemories" BEGIN
                INSERT INTO "AssistantMemorySearch"(rowid, "Content", "Owner", "Kind")
                VALUES (new."Id", new."Content", new."Owner", new."Kind");
            END;

            CREATE TRIGGER "AssistantMemories_ad" AFTER DELETE ON "AssistantMemories" BEGIN
                INSERT INTO "AssistantMemorySearch"("AssistantMemorySearch", rowid, "Content", "Owner", "Kind")
                VALUES ('delete', old."Id", old."Content", old."Owner", old."Kind");
            END;

            CREATE TRIGGER "AssistantMemories_au"
            AFTER UPDATE OF "Content", "Owner", "Kind" ON "AssistantMemories" BEGIN
                INSERT INTO "AssistantMemorySearch"("AssistantMemorySearch", rowid, "Content", "Owner", "Kind")
                VALUES ('delete', old."Id", old."Content", old."Owner", old."Kind");
                INSERT INTO "AssistantMemorySearch"(rowid, "Content", "Owner", "Kind")
                VALUES (new."Id", new."Content", new."Owner", new."Kind");
            END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "AssistantMemories_au";
            DROP TRIGGER IF EXISTS "AssistantMemories_ad";
            DROP TRIGGER IF EXISTS "AssistantMemories_ai";
            DROP TABLE IF EXISTS "AssistantMemorySearch";
            """);

        migrationBuilder.DropTable(
            name: "AssistantMemoryEdits");

        migrationBuilder.DropTable(
            name: "AssistantMemories");
    }
}
