// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>What one outbound request actually produced.</summary>
/// <remarks>
/// <para>
/// The transport's own answer, and the one shape both callers read. <see cref="HttpFetchResponse"/> is
/// the page's projection of it — allow-listed headers, text decoded by the declared charset — because a
/// page wants text and a *view* of the site wants the response exactly as it was served: every header,
/// the body's own bytes, and the hops it took to get there.
/// </para>
/// <para>
/// A caller that follows redirects gets the last response and the hops beside it, so a `302` to
/// <c>/login</c> cannot look like a page that renders a sign-in form.
/// </para>
/// </remarks>
/// <param name="Status">The final response's status, or <c>0</c> when no response arrived.</param>
/// <param name="Headers">Every response header, from the response and its content.</param>
/// <param name="Body">The body's bytes, up to the configured response cap.</param>
/// <param name="Failure">Why the fetch failed, or <see cref="HttpFetchFailure.None"/>.</param>
/// <param name="Truncated">Whether the body hit the host's response cap.</param>
/// <param name="Redirects">Every hop that was followed, in order, oldest first.</param>
internal sealed record OutboundResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body,
    HttpFetchFailure Failure,
    bool Truncated,
    IReadOnlyList<OutboundRedirect> Redirects);

/// <summary>One redirect an outbound request followed.</summary>
/// <param name="Status">The status that sent the request on.</param>
/// <param name="Location">Where it was sent, as the response named it.</param>
internal sealed record OutboundRedirect(int Status, string Location);
