// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One immutable saved revision, with the compile result built from it.</summary>
internal sealed class StoredRevision {
    public required string RevisionId { get; init; }

    public required string Path { get; init; }

    public required string Hash { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    public required string Content { get; init; }

    public required string CreationRuntimeId { get; init; }

    public string? ContentHash { get; set; }

    public string? RuntimeId { get; set; }

    public byte[]? Image { get; set; }

    public byte[]? Symbols { get; set; }

    public string? Diagnostics { get; set; }
}
