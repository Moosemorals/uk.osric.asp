<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Outbound headers are an allow-list, so a page cannot sign a request

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [outbound headers](../plans/outbound-headers.md) |
| Area | Sdk, Web (pages), docs |
| Location | `src/uk.osric.asp.Sdk/HttpFetchRequest.cs`, `src/uk.osric.asp.Web/Pages/PageFetchHeaders.cs`, `src/uk.osric.asp.Web/Pages/OutboundFetcher.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md` |
| Spike | — |

## Idea

`PageFetchHeaders` decides what a page may send by listing what it may not: `Signature`, `Digest`,
`Date` and `Host` are refused, along with `Cookie` and the framing headers, and a request that names
one is an `InvalidRequest` that is never made. That makes the host
the arbiter of every protocol a page might speak: a protocol that signs its headers cannot be
implemented by a page at all, and every new one needs a host release.

The dangerous names are few and known — the request's authority, cookies, the connection and framing
controls, the body's length and encoding, and proxy authority. Everything else is the page's own
business, and the cost of admitting it is that the page can make a request the remote end refuses,
which is the page's problem and not a security boundary.

Two more things are wrong in the same path:

- **A page's headers follow a redirect.** `SendUntilResponseAsync` re-issues the request on each hop
  with the page's headers attached, so a signed request replays its signature and digest to whatever
  host a `3xx` names. The credential is dropped on a cross-host hop; the page's own headers are not.
- **A header the transport refuses disappears.** `TryAddWithoutValidation` answers `false` for a
  malformed value, and the code ignores the answer, so the page believes it sent a header the wire
  never carried.

A `Signature` member on `HttpFetchRequest` was considered and rejected: the host would choose `Date`
and `Digest`, so it would have to tell the page them before the page signs — splitting one protocol
message across two authorities for no gain. Headers are the shape.

## Sketch

1. **The deny list.** `PageFetchHeaders` names the names a page may not set: `Host`, `Cookie`,
   `Connection`, `Content-Encoding`, `Content-Length`, `Expect`, `Keep-Alive`, `Proxy-Authorization`,
   `Proxy-Connection`, `Set-Cookie`, `TE`, `Trailer`, `Transfer-Encoding`, `Upgrade`. Every other name
   is sent, `X-` or not. `User-Agent` stays special: accepted, then replaced by the site's own.
2. **The body's headers go on the body.** A name the request headers refuse is offered to the content
   headers when there is a body, so `Content-Language` and friends land where the transport expects
   them; a name neither accepts is an `InvalidRequest` before the request is made.
3. **The reply counts.** A `TryAddWithoutValidation` that answers `false` fails the request rather
   than being ignored.
4. **Opting out of redirects.** `HttpFetchRequest.FollowRedirects` (default `true`); `false` answers
   the `3xx` itself, `Location` and all, after one request. A page that signs its request sets it.
5. **A hop that leaves the origin carries none of the page's headers.** On a redirect to another host
   or to plaintext the page's headers are dropped, exactly as the credential is; what remains is the
   host's own `User-Agent`. A page that wants its headers to travel drives the hops itself.
6. **The manual and the record.** `/help`'s fetch section and [pages](../pages.md#page-contract) say
   what may be sent, and the merge records the two names the plan intends.

## Not in scope

- **The allowance.** How many requests a render or a job may make, and how a page reads that, is
  [`fetch-is-a-rate`](fetch-is-a-rate.md) and [`the-site-is-one-type`](the-site-is-one-type.md).
- **What a page reads back.** The response header subset a fetch answers is a presentation decision
  and is unchanged; a page verifying a signature reads the body.
- **The primitives.** RSA and Ed25519 are [`the-page-api-signs`](the-page-api-signs.md); this item
  only lets the page send what it computed.

## Open questions

None. The owner settled the shape on 2026-11-22: a deny list rather than an allow-list, and no
`Signature` member on the request.

**Origin:** the owner's asks 1 and 3 of 2026-11-22, from a production node whose federation stopped
at `401` because it could not sign a delivery.
