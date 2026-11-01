<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-002 — A page cannot call an outbound HTTP API

| | |
| --- | --- |
| Size | Medium |
| Area | Core, Web, security |
| Location | `src/uk.osric.asp.Core/IContext.cs`, `src/uk.osric.asp.Web/SimpleContext.cs` |

## Idea

`IContext` can read a request and shape a response but cannot reach anything: no `HttpClient`, no
`Socket`, no `System.Net` at all — `PageApiPolicy` bans them. So a page can render what it was
given and nothing else. Anything that wants to show weather, a feed, a status, or any other live
data has to be baked in at compile time.

A `Fetch` on `IContext` would be the single most useful addition to the page API. It also needs to
be the most carefully bounded thing on it, because it turns a sandbox into a network client.

## Sketch

1. Keep `Core` pure: add request/response records (`PageFetchRequest`, `PageFetchResponse`) and a
   method on `IContext` that takes and returns them. No `HttpClient` in `Core` — `SimpleContext`
   owns that.
2. `GET` and `POST` only, with a method allow-list rather than passing a string through.
3. Host allow-list in configuration beside `Pages:ReservedPaths`, e.g. `Pages:Fetch:AllowedHosts`,
   compared ordinally and case-insensitively, matched on host not on a prefix of the URL. Refuse
   anything else before a connection is made.
4. Bound it like a render: 10-second connect+read by default, a body cap (say 1 MiB), no redirect
   following to a host that is not also allowed, and the render deadline in
   `RenderOptions` still wins.
5. Response is a plain value: status, a filtered header dictionary, and the body as text with the
   charset from the response. Nothing is cached — that is IDEA-003's job, not this one's.
6. Decide explicitly whether a failed fetch throws or returns a value. Given the rest of the page
   API, a value (`PageFetchResponse` with a status of 0 or an error string) is more in keeping with
   "pages get values, not exceptions" — but a DNS failure is not a `404`, so the shape needs thought.
7. Tests: an allow-listed host succeeds, a non-listed host is refused without a socket, a slow host
   times out, a huge body is cut, and a page that fetches still respects the render deadline.
