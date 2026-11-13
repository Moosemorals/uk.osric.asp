// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>The outcome of generating handler source from a page template.</summary>
/// <param name="Source">The generated source, or <see langword="null"/> when the template did not parse.</param>
/// <param name="Errors">
/// The problems that stopped generation, already positioned in template space; empty when
/// <paramref name="Source"/> was generated.
/// </param>
public sealed record TemplateGenerationResult(GeneratedSource? Source, IReadOnlyList<PageDiagnostic> Errors) {

    /// <summary>Whether the template contains anything the tokenizer reads as code.</summary>
    /// <remarks>
    /// True when any token other than a text token appears, and true for a template that could not
    /// be tokenized at all: a template the tokenizer refuses must never be mistaken for one that has
    /// nothing to compile.
    /// </remarks>
    public bool HasCode { get; init; }
}
