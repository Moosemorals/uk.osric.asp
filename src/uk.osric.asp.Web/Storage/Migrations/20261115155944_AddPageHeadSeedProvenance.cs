// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

#nullable disable

namespace uk.osric.asp.Web.Storage.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Gives a page's head the two content hashes seed provenance needs.</summary>
/// <remarks>
/// Both are nullable and null for every row that exists: the anchor cannot be recovered, and the
/// content hash is filled by the startup repair. A null anchor is <em>unknown</em>, never
/// "unchanged", so no existing page becomes eligible for an automatic seed write.
/// </remarks>
internal partial class AddPageHeadSeedProvenance : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.AddColumn<string>(
            name: "ContentHash",
            table: "PageHeads",
            type: "TEXT",
            nullable: true,
            collation: "BINARY");

        migrationBuilder.AddColumn<string>(
            name: "SeedHash",
            table: "PageHeads",
            type: "TEXT",
            nullable: true,
            collation: "BINARY");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropColumn(
            name: "ContentHash",
            table: "PageHeads");

        migrationBuilder.DropColumn(
            name: "SeedHash",
            table: "PageHeads");
    }
}
