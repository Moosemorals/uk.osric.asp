// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>A page's template, the image built from it, and the revision it will be saved as.</summary>
/// <param name="Path">The canonical key the page is stored under.</param>
/// <param name="Template">The template text.</param>
/// <param name="Image">
/// The image built from the template, or <see langword="null"/> when this build has never compiled
/// it. A template without one is compiled on its next request.
/// </param>
internal sealed record PageTemplate {

    public PageTemplate(PagePath Path, string Template, PageImage? Image = null) {
        ArgumentNullException.ThrowIfNull(Path);
        ArgumentNullException.ThrowIfNull(Template);

        this.Path = Path;
        this.Template = Template;
        this.Image = Image;
        RevisionId = Guid.NewGuid().ToString("N");
        CreationRuntimeId = Image?.RuntimeId ?? "legacy:unknown";
        SavedAt = DateTimeOffset.UtcNow;
        Hash = RevisionHash.Of(Template, CreationRuntimeId, SavedAt, RevisionId);
    }

    public PagePath Path { get; init; }

    public string Template { get; init; }

    public PageImage? Image { get; init; }

    public string RevisionId { get; init; }

    public string Hash { get; init; }

    public string CreationRuntimeId { get; init; }

    public DateTimeOffset SavedAt { get; init; }
}
