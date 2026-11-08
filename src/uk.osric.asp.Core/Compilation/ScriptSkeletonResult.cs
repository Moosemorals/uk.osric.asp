// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>A page's literal text, with template constructs replaced so a JavaScript parser can
/// read it without changing any position.</summary>
/// <param name="Source">The template's text with every template construct blanked, or an empty string
/// when the template could not be tokenized.</param>
/// <param name="Diagnostics">The tokenizer's errors, already positioned in template space; empty when
/// a skeleton was produced.</param>
public sealed record ScriptSkeletonResult(
    string Source,
    IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the template was tokenized and a skeleton produced.</summary>
    public bool IsSuccess => Diagnostics.Count == 0;
}
