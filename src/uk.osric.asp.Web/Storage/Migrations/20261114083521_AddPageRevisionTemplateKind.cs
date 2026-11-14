// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Turns the has-code verdict into the kind of page a revision is.</summary>
/// <remarks>
/// <para>
/// The column is renamed rather than copied, because the two answers are ordered the same way: a
/// stored <c>0</c> meant "no code" and is <c>Text</c>, a stored <c>1</c> meant "has code" and is
/// <c>Page</c>, and <c>NULL</c> still means never judged. No row can hold <c>Library</c> yet.
/// </para>
/// <para>
/// Nullable on purpose, as it was: a revision nobody has generated stays unjudged until something
/// generates it again, and there is no backfill pass.
/// </para>
/// </remarks>
internal partial class AddPageRevisionTemplateKind : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.RenameColumn(
            name: "TemplateHasCode",
            table: "PageRevisions",
            newName: "TemplateKind");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        // A library is code, so it reads as "has code" to the column this replaces. Which kind a
        // revision was is not recoverable, which is why going back recompiles a library as a page.
        migrationBuilder.Sql("UPDATE PageRevisions SET TemplateKind = 1 WHERE TemplateKind = 2;");

        migrationBuilder.RenameColumn(
            name: "TemplateKind",
            table: "PageRevisions",
            newName: "TemplateHasCode");
    }
}
