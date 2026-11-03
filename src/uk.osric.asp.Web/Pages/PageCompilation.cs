// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;

/// <summary>The compile result stored beside a page's source.</summary>
/// <param name="ContentHash">The fingerprint of the source the image was built from.</param>
/// <param name="RuntimeId">The build that produced the image.</param>
/// <param name="Image">The emitted assembly, or <see langword="null"/> when compilation failed.</param>
/// <param name="Symbols">The emitted portable PDB, or <see langword="null"/> when compilation failed.</param>
/// <param name="Diagnostics">
/// The compiler's author-facing diagnostics, errors and warnings, in template space; empty when the
/// page compiled cleanly.
/// </param>
internal sealed record PageCompilation(
    string ContentHash,
    string RuntimeId,
    byte[]? Image,
    byte[]? Symbols,
    IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the stored result still matches the content and the build it came from.</summary>
    /// <param name="contentHash">The fingerprint of the source as it is now.</param>
    /// <param name="runtimeId">The build that is running now.</param>
    public bool IsCurrent(string contentHash, string runtimeId) {
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(runtimeId);

        return string.Equals(ContentHash, contentHash, StringComparison.Ordinal)
            && string.Equals(RuntimeId, runtimeId, StringComparison.Ordinal)
            && (Image is null || Symbols is not null);
    }
}
