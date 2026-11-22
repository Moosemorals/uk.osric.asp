<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot see its request body, its raw target or a repeated header

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Sdk, Web (pages), docs |
| Location | `src/uk.osric.asp.Sdk/PageRequest.cs`, `src/uk.osric.asp.Web/Pages/PageRequestSnapshot.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md` |
| Spike | — |

## Idea

`PageRequest` carries the method, the path, the query, the form, the headers and the cookies. Three
things it does not carry are the three a signature is built from:

- **The body.** The shell reads a request body only when it is a form, and only to parse it. A `POST`
  that is not a form is never read, so a page cannot hash the bytes it was sent, and a `Digest` it
  computes can never be checked against them.
- **The raw target.** HTTP signatures sign `(request-target)` — the method, the path and the query
  exactly as sent. `PageRequest` has the method and the canonical path, and the query is parsed,
  collapsed and case-folded; the raw query string is gone.
- **The repeated header.** `Headers` keeps the first value of a name; a `Signature` sent twice, or any
  header a sender repeated, arrives halved.

The header *names* are already visible in full: `PageRequestSnapshot` copies every header the request
carried, including `Signature`, `Digest`, `Date` and `Host`. The gap is fidelity, not access — which
is why the fix is three values rather than a capability.

The body needs a bound, and the bound already exists: the form reader buffers to 1 MiB
(`MaxFormBytes`), refusing a larger body with `413`. That number is an invariant
(`policy-is-a-constant`), not a key, and the new read shares it rather than growing one.

## Sketch

1. **`Body` as bytes.** `PageRequest.Body` is the request's bytes, empty when the method cannot carry a
   body, buffered through the same read and the same 1 MiB cap the form uses, and handed to the page
   before the handler runs. A form post keeps its parsed `Form`; the bytes are beside it, not instead
   of it.
2. **`RawTarget` as text.** The path and query as the client wrote them, from
   `IHttpRequestFeature.RawTarget`, so the canonical string can be rebuilt. `Path` stays the
   canonical key; the two are different facts and both are wanted.
3. **`HeaderValues` beside `Headers`.** Every value of a name in the order sent, as `QueryValues`
   sits beside `Query`, so a repeated header is not lost. `Headers` keeps its first-value rule.
4. **The tests pin the rule.** A signed `POST` rebuilds its canonical string from the snapshot and
   hashes the body to the `Digest` it was sent; a repeated header keeps both values; a body over the
   cap is the same `413` the form already answers.
5. **The manual and the record.** `/help`'s request section and
   [pages](../pages.md#page-contract) say what a page sees, and the merge records the name the plan
   intends.

## Not in scope

- **A response body a page cannot hold.** The response is text with a character budget; a binary
  response is a different item.
- **A multipart upload.** `PageRequest.Files` is a whim ([`whim-page-file-upload`](WHIMS.md)), not this
  read.
- **The primitives and the headers.** RSA is [`the-page-api-signs`](the-page-api-signs.md); sending a
  signed header is [`outbound-headers-are-an-allow-list`](outbound-headers-are-an-allow-list.md).

## Open questions

None. That a page should see its request as it was sent is the owner's instruction of 2026-11-22; the
shape is the three values above.

**Origin:** the owner's ask 3 of 2026-11-22, whose node could not canonicalise an inbound request
because the body bytes and the raw target were not in the contract.
