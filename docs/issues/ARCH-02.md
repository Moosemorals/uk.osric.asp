<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-02 — `IContext` is too narrow to be the page runtime's public API

| | |
| --- | --- |
| Severity | High |
| Area | Architecture, runtime API |
| Location | `src/uk.osric.asp.Core/IContext.cs`, `src/uk.osric.asp.Web/MainEndpoint.cs:22-46` |

## Problem

Pages receive only `Log`, `Write`, `WriteRaw` and `Path`. They cannot:

- read the request method, query string, form, headers or cookies;
- set the status code, content type, response headers or redirects;
- access a cancellation token (see ARCH-03).

`MainEndpoint` also sets status 200 and the content type *before* running the handler and buffers the
whole response in a `StringWriter`, so a page can neither influence the response metadata nor stream
output. A `POST` to a page runs it, but the form body is unreadable, so forms and search pages are
impossible.

`IContext` is the contract every stored page is written against. Changing it after content exists
means rewriting pages, so the decision is urgent even if the implementation is not.

## Fix outline

1. Decide the product shape explicitly: an HTML page renderer (current behaviour) or a general
   endpoint runtime that can inspect requests and control responses.
2. If a renderer, document that pages are GET-only and must not inspect the request; enforce it by
   routing only safe methods and rejecting others with 405.
3. If a runtime, extend the contract with plain values, not ASP.NET types:
   - `RequestMethod`, `Query` (`IReadOnlyDictionary<string, string>`), `Form`, `Headers`, `Path`;
   - `SetStatus(int)`, `SetContentType(string)`, `Redirect(string)`.
   Keep the core free of `HttpContext` by having `SimpleContext` adapt a request snapshot.
4. Run the handler before writing the response, then set status and headers from what the page
   requested; write to `Response.Body` rather than a full buffer (or keep buffering only so that a
   throwing page does not emit half a document — document the choice).
5. Version the contract: add `IPageRequest`/`IPageResponse`-style records in `Core`, and keep
   `IContext` as the facade so existing pages keep compiling.
6. Cover the new surface with integration tests (ARCH-08), because this is exactly the seam that is
   currently untested.
