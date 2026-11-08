// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Errors;

/// <summary>What a page and its layouts came to.</summary>
internal abstract record PageChainResult {

    private PageChainResult() { }

    /// <summary>The chain produced a document.</summary>
    /// <param name="Response">The response the content owns, with every layer's script permission.</param>
    /// <param name="Body">The outermost layer's body.</param>
    public sealed record Served(PageResponse Response, string Body) : PageChainResult;

    /// <summary>The chain could not be built, so there is a failure to report instead of a document.</summary>
    /// <param name="StatusCode">The status to send.</param>
    /// <param name="Error">What to tell the reader.</param>
    public sealed record Failed(int StatusCode, ErrorMessage Error) : PageChainResult;
}
