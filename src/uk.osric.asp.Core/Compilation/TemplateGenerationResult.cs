// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

using uk.osric.asp.Core.Pages;

/// <summary>The outcome of generating handler source from a page template.</summary>
/// <param name="Source">The generated source, or <see langword="null"/> when the template did not parse.</param>
/// <param name="Errors">
/// The problems that stopped generation, already positioned in template space; empty when
/// <paramref name="Source"/> was generated.
/// </param>
public sealed record TemplateGenerationResult(GeneratedSource? Source, IReadOnlyList<PageDiagnostic> Errors) {

    /// <summary>What the template is for, judged from its tokens.</summary>
    /// <remarks>
    /// <para>
    /// A template with no code is <see cref="PageKind.Text"/>, one with only declaration blocks is a
    /// <see cref="PageKind.Library"/>, and anything else renders as a <see cref="PageKind.Page"/>.
    /// The verdict is required rather than defaulted, so a caller cannot forget it and leave a page
    /// looking like an asset.
    /// </para>
    /// <para>
    /// A template that could not be tokenized or generated is a page, whatever it started as: it has
    /// code the tokenizer saw, and a failure must never be mistaken for something with nothing to
    /// compile.
    /// </para>
    /// </remarks>
    public required PageKind Kind { get; init; }
}
