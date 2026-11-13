// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;

/// <summary>The image compiled from a page's template, stored beside it.</summary>
/// <param name="TemplateHash">The fingerprint of the template the image was built from.</param>
/// <param name="RuntimeId">The build that produced the image.</param>
/// <param name="Bytes">The emitted assembly, or <see langword="null"/> when compilation failed or there was nothing to compile.</param>
/// <param name="Symbols">The emitted portable PDB, or <see langword="null"/> when compilation failed.</param>
/// <param name="Diagnostics">
/// The compiler's author-facing diagnostics, errors and warnings, in template space; empty when the
/// template compiled cleanly.
/// </param>
/// <param name="HasCode">
/// Whether the template had anything to compile, or <see langword="null"/> when the stored row has
/// never been judged. A record built by generation always knows; a stored image from before the
/// verdict existed keeps the null.
/// </param>
internal sealed record PageImage(
    string TemplateHash,
    string RuntimeId,
    byte[]? Bytes,
    byte[]? Symbols,
    IReadOnlyList<PageDiagnostic> Diagnostics,
    bool? HasCode = true) {

    /// <summary>Whether the stored image still matches the template and the build it came from.</summary>
    /// <param name="templateHash">The fingerprint of the template as it is now.</param>
    /// <param name="runtimeId">The build that is running now.</param>
    public bool IsCurrent(string templateHash, string runtimeId) {
        ArgumentNullException.ThrowIfNull(templateHash);
        ArgumentNullException.ThrowIfNull(runtimeId);

        return string.Equals(TemplateHash, templateHash, StringComparison.Ordinal)
            && string.Equals(RuntimeId, runtimeId, StringComparison.Ordinal)
            && (Bytes is null || Symbols is not null);
    }
}
