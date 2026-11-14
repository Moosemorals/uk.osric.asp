// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Adds the fingerprint of the libraries an image was compiled against.</summary>
/// <remarks>
/// Nullable, and null for every row that exists: a page that names no library has no closure, and a
/// page compiled before libraries existed is stale on its next request anyway. The value is opaque —
/// a sorted <c>key@revisionHash</c> per library — and is compared with the live heads, never read as
/// data.
/// </remarks>
internal partial class AddPageRevisionTemplateLibraries : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<string>(
            name: "TemplateLibraries",
            table: "PageRevisions",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "TemplateLibraries",
            table: "PageRevisions");
    }
}
