// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Errors;

/// <summary>A chain that could not be built, worded for the reader of the page that asked for it.</summary>
/// <remarks>
/// The endpoint turns this into the error document for the page the visitor asked for; an embed
/// turns it into a <see cref="uk.osric.asp.Sdk.UserFacingException"/> the page may catch, because an
/// embedded page's failure never reached the caller's body.
/// </remarks>
/// <param name="statusCode">The status the top-level page would answer with.</param>
/// <param name="error">What to tell the reader.</param>
internal sealed class PageChainFailureException(int statusCode, ErrorMessage error) : Exception(error?.Summary) {

    /// <summary>The status the top-level page answers with.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>What to tell the reader.</summary>
    public ErrorMessage Error { get; } = error;
}
