<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Outbound headers

| | |
| --- | --- |
| Status | Proposed 2026-11-22 — waits for the owner (`product-steps-wait-for-the-owner`). Nothing below is built |
| Size | Medium — one SDK member, one replaced policy list, one attempt path, and the tests that pin the wire |
| Area | Sdk, Web (pages), docs |
| Spike | None. Every rule is a pure decision over the request's headers, and `StubHttpMessageHandler` already asserts what reaches the wire |
| Backlog | [`outbound-headers-are-an-allow-list`](../backlog/outbound-headers-are-an-allow-list.md) is this job |
| Decisions | Two names intended, below; taken at the merge (`product-plan-before-building`) |

## The short version

A page may send any header it likes, except the few names that belong to the transport, and a header it
sets belongs to the origin it was set for:

- **A deny list replaces the allow-list.** `Host`, `Cookie` and the connection and framing controls
  are refused; `Signature`, `Digest`, `Date` and every other name travel. `X-` stops being the only
  custom prefix because every name is custom now.
- **No `Signature` member.** The page computes `Digest` and `Date` from a body it chooses and a clock
  it can read; a host-computed pair would have to be given back to the page before it could sign.
- **A request may refuse to follow.** `HttpFetchRequest.FollowRedirects = false` answers the `3xx`
  itself, which is what a signed request needs.
- **A hop that leaves the origin carries none of the page's headers.** The page's headers go the way
  the credential already goes; a page that wants them to travel drives the hops itself.
- **A header the transport refuses fails the request.** A page never believes it sent something the
  wire did not carry.

## The problem

`PageFetchHeaders.IsRequestAllowed` is an allow-list of eleven names plus `X-`. A page asking for a
federated delivery must send `Signature`, `Digest`, `Date` and `Host` — the first three because the
signature's header set names them, the fourth because a peer checks that the signed headers cover the
authority it reached. All four are refused with `InvalidRequest`, so no delivery can be signed and
the peer answers `401`.

The list is the wrong shape for a general reason: it makes the host the arbiter of every protocol a
page may speak. A page is arbitrary C# with the network already open through `IHttpFetch`; the set of
header names that turns a permitted request into a new capability is small and stable, and everything
else is the page's own business. A remote that trusts a header the page invented is trusting the page,
which is already the site's own code.

Two defects sit in the same path and are fixed here because the same tests cover them:

- **The headers follow the redirect.** `SendUntilResponseAsync` re-issues the request on each hop with
  `request.Headers` attached, so a `3xx` to another host replays a signature, a digest and any token
  the page set. The named `Credential` is dropped correctly; the page's own headers are not.
- **A refused header is silent.** `CreateRequestMessage` ignores `TryAddWithoutValidation`'s answer, so
  a malformed value (a `Date` the transport cannot parse is the common one) is dropped and the page
  signs a request that does not contain what it signed.

## The shape

### The deny list

`PageFetchHeaders` names what a page may not set, and why each one is the transport's:

| Name | Why |
| --- | --- |
| `Host` | It is the authority the request is sent to; a page that could set it would claim another virtual host on a shared address |
| `Cookie` | The host has no jar and carries no ambient credential; a page that means to send one sets `Authorization` or names a credential |
| `Set-Cookie` | A response header; a request naming it is a mistake |
| `Connection`, `Keep-Alive`, `Proxy-Connection`, `TE`, `Trailer`, `Transfer-Encoding`, `Upgrade` | Connection and framing control belongs to the transport |
| `Expect` | `100-continue` changes the transport's handshake |
| `Content-Length` | The host measures the body it encodes |
| `Content-Encoding` | The body is UTF-8 text; a page cannot gzip what it cannot encode |
| `Proxy-Authorization` | The site uses no proxy credential, and a page must not pick one |

Every other name is sent, including `Authorization`, `Signature`, `Digest`, `Date`, `Content-Type`
and any `X-` name. `User-Agent` is accepted and replaced by the site's own, as it is today.

`Content-*` names other than `Content-Type` are offered to the content headers when the request has a
body, so `Content-Language` lands where the transport expects it; `Content-Type` keeps its existing
job of choosing the body's media type. A name that neither header collection accepts is
`InvalidRequest`, as is a value with a line break.

### Redirects

`HttpFetchRequest.FollowRedirects` defaults to `true`, so nothing changes for a page that does not ask.
`false` means one request: the `3xx` is the answer, with its `Location` (already visible to a page
through the response header list) and whatever body it carried.

When a hop leaves the request's origin — a different scheme, host or port — the page's headers are not
carried to it. The hop is still taken, the host's `User-Agent` is still set, and the named credential
is still dropped by the rule that already exists. This is one rule rather than a list of "identity
headers" to drop: the page's headers were for the origin it named, and a redirect to another origin is
a different place. A page that needs its headers to survive a hop sets `FollowRedirects = false` and
makes each request itself.

### The attempt

Today `PageHttpFetch` spends the allowance before `OutboundFetcher` validates the request, so a
refused request costs a token; that is
[`fetch-is-a-rate`](../backlog/fetch-is-a-rate.md)'s to change, and it will need the policy's answer
before it takes one. This plan leaves the validation and the denial where they are, in
`OutboundFetcher`, and leaves the charge where it is, so the two changes do not fight over the same
lines.

## What changes

| Where | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/HttpFetchRequest.cs` | `FollowRedirects`, default `true`, documented with why a signed request sets it false |
| `src/uk.osric.asp.Web/Pages/PageFetchHeaders.cs` | The allow-list becomes the deny list above; the response list is unchanged |
| `src/uk.osric.asp.Web/Pages/OutboundFetcher.cs` | Validate then send; route a refused request header to the content headers; fail on a refused value; drop the page's headers on an origin change; honour `FollowRedirects` |
| `src/uk.osric.asp.Web/Pages/PageHttpFetch.cs` | Nothing, except what `fetch-is-a-rate` does to the charge |
| `test/uk.osric.asp.Web.Tests/PageFetcherTests.cs` | The tests below; the existing disallowed-header cases keep `Cookie`, `Host` and `Transfer-Encoding` and lose the `X-` assumption |
| `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md` | What a page may send, that a redirect does not carry it, and how to opt out |
| `docs/decisions.md` | The two entries, written by the merge |

## Tests

- **A signed request goes out as written.** A `POST` with `Signature`, `Digest`, `Date` and
  `Content-Type` reaches the stub with each value byte-for-byte, and the body's bytes are exactly
  `UTF8(Body)`, so the `Digest` the page computed matches what the wire carried.
- **The transport's names are still refused.** `Host`, `Cookie`, `Transfer-Encoding`, `Content-Length`
  and `Proxy-Authorization` are each `InvalidRequest`, and the stub records no request.
- **A malformed value fails rather than disappearing.** `Date: not-a-date` is `InvalidRequest` and no
  request is made.
- **A content header lands on the content.** `Content-Language` is on the content headers of the
  request the stub sees, not lost and not on the request headers.
- **No follow means one request.** `FollowRedirects = false` against a `302` answers `302` with its
  `Location`, and the stub saw exactly one request.
- **A hop keeps what stayed.** A `302` to the same origin still carries the page's headers when
  following is on.
- **A hop drops what left.** A `302` to another host carries none of them, and the named credential's
  existing rule still applies on the same hop.
- **The user agent is still the site's.** A page that sets one is accepted and replaced.

## Docs

- `/help`'s fetch section says the deny list's shape rather than an allow-list, names `FollowRedirects`,
  and says a redirect does not carry the page's headers.
- [pages](../pages.md#page-contract) says the same in the outbound-fetch bullet.
- The merge writes **pages-outbound-headers-are-a-deny-list** and
  **pages-a-redirect-carries-no-page-headers**, and points `pages-outbound-fetch` at them.

## Risks

- **A name the list misses.** The list is the transport's, and a name that escapes it can only change
  how the page's own request is framed; the capability being granted is the request, already open
  (`pages-outbound-fetch`). `Host` is the one that matters and is named.
- **A page that relied on a refusal.** Nothing in the contract promised a name was refused, and a page
  that set one today got `InvalidRequest`; after this it gets a request. That is the point, and it is a
  widening of what a page may do rather than a change to what its code means.
- **A redirect that silently loses a header.** It is visible in the response the page gets back (the
  final status and body), and `FollowRedirects = false` is the explicit way to see every hop. A
  redirect record on the response is a later idea if it is wanted.

## Open questions

None. The owner settled the shape on 2026-11-22; the remaining choices are the deny list's exact
contents, which this plan states, and they are reviewed here rather than asked again.
