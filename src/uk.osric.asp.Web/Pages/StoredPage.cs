// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>A page's source together with the compile result stored beside it.</summary>
/// <param name="Path">The canonical key the page is stored under.</param>
/// <param name="Content">The template source.</param>
/// <param name="Compilation">
/// The stored compile result, or <see langword="null"/> when the page has never been compiled by
/// this build. A page without one is compiled on its next request.
/// </param>
internal sealed record StoredPage {

    public StoredPage(PagePath Path, string Content, PageCompilation? Compilation = null) {
        ArgumentNullException.ThrowIfNull(Path);
        ArgumentNullException.ThrowIfNull(Content);

        this.Path = Path;
        this.Content = Content;
        this.Compilation = Compilation;
        RevisionId = Guid.NewGuid().ToString("N");
        CreationRuntimeId = Compilation?.RuntimeId ?? "legacy:unknown";
        SavedAt = DateTimeOffset.UtcNow;
        Hash = PageRevisionHash.Of(Content, CreationRuntimeId, SavedAt, RevisionId);
    }

    public PagePath Path { get; init; }

    public string Content { get; init; }

    public PageCompilation? Compilation { get; init; }

    public string RevisionId { get; init; }

    public string Hash { get; init; }

    public string CreationRuntimeId { get; init; }

    public DateTimeOffset SavedAt { get; init; }
}
