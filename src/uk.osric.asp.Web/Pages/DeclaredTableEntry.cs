// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One table a source declared, as it was last applied.</summary>
/// <remarks>
/// The catalogue is what makes a save a difference rather than a rebuild: it holds what the source
/// declared when the table was made, so the next save knows what to add and what the source no longer
/// names. A move re-keys <see cref="SourceKey"/> and leaves <see cref="RealName"/> alone, so the rows
/// stay where they are; a scrub finds what an older build left behind.
/// </remarks>
internal sealed class DeclaredTableEntry {
    public long Id { get; init; }

    /// <summary>The canonical key of the source that declares the table.</summary>
    public required string SourceKey { get; init; }

    /// <summary>The name the source writes in SQL.</summary>
    public required string Name { get; init; }

    /// <summary>The stored <c>StoreSubject</c>: <c>0</c> is the page's, <c>1</c> one visitor's.</summary>
    public required int Subject { get; init; }

    /// <summary>The stored <c>StoreLifetimeKind</c>; <c>Page</c> for a page's table.</summary>
    public required int Lifetime { get; init; }

    /// <summary>The declaration as JSON, compared to decide what a save adds and what it drops.</summary>
    public required string Columns { get; set; }

    /// <summary>The one column the search matches, or <see langword="null"/>.</summary>
    public string? Search { get; init; }

    /// <summary>The unguessable name the table really has in <c>main</c>.</summary>
    public required string RealName { get; init; }
}

/// <summary>One table a source declared, as the store reads it back.</summary>
/// <param name="Name">The name the source writes.</param>
/// <param name="Subject">Whose rows it holds.</param>
/// <param name="Lifetime">How long a visitor's rows live.</param>
/// <param name="Columns">The declaration.</param>
/// <param name="RealName">The unguessable name in <c>main</c>.</param>
/// <param name="Search">The search column, when there is one.</param>
internal sealed record DeclaredTable(
    string Name,
    int Subject,
    int Lifetime,
    string Columns,
    string RealName,
    string? Search);
