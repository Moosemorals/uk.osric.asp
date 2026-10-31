// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

public sealed class StoredFile {
    public required string Path { get; init; }

    public required string Content { get; set; }

    /// <summary>Fingerprint of <see cref="Content"/>; null when the row predates the cache.</summary>
    public string? ContentHash { get; set; }

    /// <summary>The build that produced <see cref="Image"/>; null when the page was never compiled.</summary>
    public string? RuntimeId { get; set; }

    /// <summary>The emitted assembly; null when compilation failed.</summary>
    public byte[]? Image { get; set; }

    /// <summary>The compiler's messages as a JSON array; null when the page compiled.</summary>
    public string? Diagnostics { get; set; }
}
