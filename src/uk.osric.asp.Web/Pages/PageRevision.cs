// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One immutable saved revision: a template, and the image built from it.</summary>
internal sealed class PageRevision {
    public required string RevisionId { get; init; }

    public required string Path { get; init; }

    public required string Hash { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    public required string Template { get; init; }

    public required string CreationRuntimeId { get; init; }

    public PageKind? TemplateKind { get; set; }

    public string? TemplateHash { get; set; }

    public string? RuntimeId { get; set; }

    public byte[]? Image { get; set; }

    public byte[]? Symbols { get; set; }

    public string? Diagnostics { get; set; }
}
