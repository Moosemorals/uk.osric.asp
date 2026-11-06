// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <inheritdoc />
internal partial class RenameTemplateColumns : Migration {

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.RenameColumn(
            name: "ContentHash",
            table: "PageRevisions",
            newName: "TemplateHash");

        migrationBuilder.RenameColumn(
            name: "Content",
            table: "PageRevisions",
            newName: "Template");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.RenameColumn(
            name: "TemplateHash",
            table: "PageRevisions",
            newName: "ContentHash");

        migrationBuilder.RenameColumn(
            name: "Template",
            table: "PageRevisions",
            newName: "Content");
    }
}
