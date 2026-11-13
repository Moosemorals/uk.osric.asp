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

    public PageTemplate(PagePath Path, string Template, PageImage? Image = null)
        : this(Path, Template, Image,
            Guid.NewGuid().ToString("N"),
            Image?.RuntimeId ?? "legacy:unknown",
            DateTimeOffset.UtcNow) {
    }

    private PageTemplate(
        PagePath path,
        string template,
        PageImage? image,
        string revisionId,
        string creationRuntimeId,
        DateTimeOffset savedAt) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(creationRuntimeId);

        Path = path;
        Template = template;
        Image = image;
        RevisionId = revisionId;
        CreationRuntimeId = creationRuntimeId;
        SavedAt = savedAt;
        Hash = RevisionHash.Of(template, creationRuntimeId, savedAt, revisionId);
    }

    public PagePath Path { get; init; }

    public string Template { get; init; }

    public PageImage? Image { get; init; }

    /// <summary>
    /// Whether the revision has anything to compile, or <see langword="null"/> when it has never
    /// been generated. <see langword="false"/> is served from source and is never compiled.
    /// </summary>
    public bool? TemplateHasCode { get; init; }

    public string RevisionId { get; init; }

    public string Hash { get; init; }

    public string CreationRuntimeId { get; init; }

    public DateTimeOffset SavedAt { get; init; }

    /// <summary>Builds a revision around a template and the image compiled from it.</summary>
    /// <param name="path">The canonical key the page is saved under.</param>
    /// <param name="source">The template text.</param>
    /// <param name="image">The compiled image, or <see langword="null"/> when there is none.</param>
    /// <param name="runtimeId">The build that compiled the image.</param>
    /// <returns>
    /// A revision with a fresh id, timestamp and hash. An editor save and an import both build
    /// revisions here, so the two paths cannot produce a row shaped differently.
    /// </returns>
    public static PageTemplate NewRevision(PagePath path, string source, PageImage? image, string runtimeId) {
        return new PageTemplate(
            path, source, image, Guid.NewGuid().ToString("N"), runtimeId, DateTimeOffset.UtcNow) {
            TemplateHasCode = image?.HasCode,
        };
    }
}
