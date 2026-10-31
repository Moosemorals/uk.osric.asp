// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

/// <summary>The compile result stored beside a page's source.</summary>
/// <param name="ContentHash">The fingerprint of the source the image was built from.</param>
/// <param name="RuntimeId">The build that produced the image.</param>
/// <param name="Image">The emitted assembly, or <see langword="null"/> when compilation failed.</param>
/// <param name="Diagnostics">The compiler's messages; empty when the page compiled.</param>
public sealed record PageCompilation(
    string ContentHash,
    string RuntimeId,
    byte[]? Image,
    IReadOnlyList<string> Diagnostics);
