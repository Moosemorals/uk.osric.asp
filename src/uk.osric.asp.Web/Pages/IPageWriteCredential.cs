// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The credential a write's commit must still find live before it stores anything.</summary>
/// <remarks>
/// A render holds its visitor's authority for the length of the request, so its writer is given none:
/// the request has already proved it. A job outlives the request that started it, so its writer is
/// given the credential the job borrowed, and the commit asks this before it touches the store. The
/// answer is the two checks the cookie's <c>OnValidatePrincipal</c> and the view's handler already
/// make, so there is one definition of "signed in".
/// </remarks>
internal interface IPageWriteCredential {

    /// <summary>Whether the credential is still one a request would accept.</summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> while the account and the handle behind it are still live.</returns>
    ValueTask<bool> IsLiveAsync(CancellationToken cancellationToken);
}
