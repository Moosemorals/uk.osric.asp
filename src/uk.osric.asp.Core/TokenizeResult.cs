// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>The tokens a template yields, and the problems that stopped tokenization.</summary>
/// <param name="Tokens">Every token taken from the template before tokenization stopped.</param>
/// <param name="Errors">
/// The problems found, already positioned in template space; empty when the whole template was
/// tokenized.
/// </param>
/// <remarks>
/// A malformed template is input rather than a fault, so it is reported as a value. That is what lets
/// a page that does not parse fail the same way as a page that does not compile: stored with its
/// diagnostics, and shown to the author in the editor.
/// </remarks>
internal sealed record TokenizeResult(IReadOnlyList<Token> Tokens, IReadOnlyList<PageDiagnostic> Errors) {

    /// <summary>Whether the whole template was tokenized.</summary>
    public bool Success => Errors.Count == 0;
}
