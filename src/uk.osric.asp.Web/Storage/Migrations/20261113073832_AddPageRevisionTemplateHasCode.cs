// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Adds the verdict that says a revision has nothing to compile.</summary>
/// <remarks>
/// Nullable on purpose: an existing revision has never been judged and stays that way until
/// something generates it again. There is no backfill pass.
/// </remarks>
internal partial class AddPageRevisionTemplateHasCode : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<bool>(
            name: "TemplateHasCode",
            table: "PageRevisions",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "TemplateHasCode",
            table: "PageRevisions");
    }
}
