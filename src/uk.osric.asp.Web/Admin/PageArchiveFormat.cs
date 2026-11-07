// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

/// <summary>The archive's identity and shape, in the one place the writer and the reader agree on.</summary>
/// <remarks>
/// <para>
/// An archive is a SQLite database rather than a zip: it already sits underneath the site's storage
/// provider, a row has a <c>Path</c> value instead of an entry name, and the format carries its own
/// version. <see cref="ApplicationId"/> is the format identity and <see cref="Version"/> the format
/// version, so a reader refuses a file before it reads a row.
/// </para>
/// <para>
/// The schema is deliberately not the site's own: it ships source and state only. Revisions carry
/// images, symbols, hashes and runtime ids that belong to the machine that built them, so they stay
/// where they were made.
/// </para>
/// </remarks>
internal static class PageArchiveFormat {

    /// <summary>The <c>application_id</c> every archive carries: the bytes <c>uoas</c>.</summary>
    public const uint ApplicationId = 0x756F6173;

    /// <summary>The format version this build writes and accepts.</summary>
    public const int Version = 1;

    /// <summary>The table holding one row per page.</summary>
    public const string PagesTable = "Pages";

    /// <summary>The table holding one row per page state key.</summary>
    public const string PageStateTable = "PageState";

    /// <summary>The statement that creates <see cref="PagesTable"/>.</summary>
    /// <remarks>
    /// <c>Path</c> is the canonical page key, root included, and <c>Source</c> is the template text
    /// exactly as stored. <c>WITHOUT ROWID</c> because the key is the whole row identity.
    /// </remarks>
    public const string CreatePages = """
        CREATE TABLE "Pages" (
            "Path"   TEXT NOT NULL PRIMARY KEY,
            "Source" TEXT NOT NULL
        ) WITHOUT ROWID
        """;

    /// <summary>The statement that creates <see cref="PageStateTable"/>.</summary>
    /// <remarks>
    /// The same shape as the site's own state table: <c>Value</c> is the stored JSON text, so a
    /// stored <c>null</c> is the four characters <c>null</c> and a page with no state has no rows.
    /// </remarks>
    public const string CreatePageState = """
        CREATE TABLE "PageState" (
            "PagePath" TEXT NOT NULL REFERENCES "Pages" ("Path") ON DELETE CASCADE,
            "Key"      TEXT NOT NULL,
            "Value"    TEXT NOT NULL,
            PRIMARY KEY ("PagePath", "Key")
        ) WITHOUT ROWID
        """;

    /// <summary>The columns <see cref="PagesTable"/> must have, in order.</summary>
    public static IReadOnlyList<ArchiveColumn> PagesColumns { get; } = [
        new("Path", NotNull: true, PrimaryKey: 1),
        new("Source", NotNull: true, PrimaryKey: 0),
    ];

    /// <summary>The columns <see cref="PageStateTable"/> must have, in order.</summary>
    public static IReadOnlyList<ArchiveColumn> PageStateColumns { get; } = [
        new("PagePath", NotNull: true, PrimaryKey: 1),
        new("Key", NotNull: true, PrimaryKey: 2),
        new("Value", NotNull: true, PrimaryKey: 0),
    ];
}

/// <summary>One column an archive table must carry, as <c>PRAGMA table_info</c> describes it.</summary>
/// <param name="Name">The column name, matched exactly.</param>
/// <param name="NotNull">Whether the column declares <c>NOT NULL</c>.</param>
/// <param name="PrimaryKey">Its 1-based position in the primary key, or 0 when it is not one.</param>
internal sealed record ArchiveColumn(string Name, bool NotNull, int PrimaryKey);
